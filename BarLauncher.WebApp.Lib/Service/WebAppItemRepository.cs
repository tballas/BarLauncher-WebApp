using FluentDataAccess;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BarLauncher.WebApp.Lib.Core.Service;
using BarLauncher.WebApp.Lib.DomainModel;

namespace BarLauncher.WebApp.Lib.Service
{
    public class WebAppItemRepository : IWebAppItemRepository
    {
        private IDataAccessService DataAccessService { get; set; }

        public WebAppItemRepository(IDataAccessService dataAccessService)
        {
            DataAccessService = dataAccessService;
        }

        private void UpgradeForProfile()
        {
            try
            {
                DataAccessService.GetQuery("select id from webapp_item").Execute();
                try
                {
                    DataAccessService.GetQuery("select profile from webapp_item").Execute();
                }
                catch (System.Exception)
                {
                    DataAccessService
                        .GetQuery(
                            "create temp table webapp_item_update (id integer primary key, url text, keywords text, search text, profile text);" +
                            "insert into webapp_item_update (id, url, keywords, search, profile) select id, url, keywords, search, 'default' from webapp_item order by id;" +
                            "drop table webapp_item;" +
                            "create table if not exists webapp_item (id integer primary key, url text, keywords text, search text, profile text);" +
                            "insert into webapp_item (id, url, keywords, search, profile) select id, url, keywords, search, profile from webapp_item_update order by id;" +
                            "drop table webapp_item_update;"
                        )
                        .Execute();
                }
            }
            catch (System.Exception)
            {
                // No updagre needed
            }

        }

        public void Init()
        {
            UpgradeForProfile();
            DataAccessService
                .GetQuery("create table if not exists webapp_item (id integer primary key, url text, keywords text, search text, profile text)")
                .Execute();
            DataAccessService
                .GetQuery("create unique index if not exists webapp_item_url on webapp_item (url)")
                .Execute();
            // BUGFIX : If older version had generated a null field for keywords, replace it by an empty string to prevent bugs.
            DataAccessService
                .GetQuery("update webapp_item set keywords='' where keywords is null")
                .Execute();
        }

        private string GetSearchField(string url, string keywords) => string.Format("{0} {1}", url, keywords).ToLower();

        private string NormalizeKeywords(string keywords) => keywords != null ? keywords : "";

        private string GetProfile(string profile) => profile ?? "default";

        public void AddItem(WebAppItem item)
        {
            DataAccessService
                .GetQuery("insert or replace into webapp_item (url, keywords, search, profile) values (@url, @keywords, @search, @profile)")
                .WithParameter("url", item.Url)
                .WithParameter("keywords", NormalizeKeywords(item.Keywords))
                .WithParameter("search", GetSearchField(item.Url, item.Keywords))
                .WithParameter("profile", GetProfile(item.Profile))
                .Execute();
        }

        public void RemoveItem(string url)
        {
            DataAccessService
                .GetQuery("delete from webapp_item where url=@url")
                .WithParameter("url", url)
                .Execute();
        }

        public IEnumerable<WebAppItem> SearchItems(IEnumerable<string> terms)
        {
            var builder = new StringBuilder("select id, url, keywords, profile from webapp_item ");
            int index = 0;
            foreach (var term in terms)
            {
                if (index == 0)
                {
                    builder.Append("where ");
                }
                else
                {
                    builder.Append("and ");
                }
                // "search" is the column in the database
                // that holds both "url" and "keywords" put together in
                // GetSearchField(url, keywords)
                // This is why it has only matched words in the url or keywords in order
                builder.Append("search like @param");
                builder.Append(index.ToString());
                builder.Append(" ");
                index++;
            }
            builder.Append("order by id");
            var dataAccessQuery = DataAccessService.GetQuery(builder.ToString());
            index = 0;
            foreach (var term in terms)
            {
                var parameterName = string.Format("param{0}", index);
                var parameterValue = string.Format("%{0}%", term.ToLower());
                dataAccessQuery = dataAccessQuery.WithParameter(parameterName, parameterValue);
                index++;
            }
            var results = dataAccessQuery
                .Returning<WebAppItem>()
                .Reading("id", (WebAppItem item, long value) => item.Id = value)
                .Reading("url", (WebAppItem item, string value) => item.Url = value)
                .Reading("keywords", (WebAppItem item, string value) => item.Keywords = value)
                .Reading("profile", (WebAppItem item, string value) => item.Profile = value)
                .Execute()
                ;
            // Check if the terms.Count() is not 0(for the "list" command)
            // else, return results
            // Replace "{q}" or "%s" with terms for web search query
            if (terms.Count() != 0)
            {
                // Just search for the keyword/url in the "search" column
                var query = "select id, url, keywords, profile from webapp_item where search like @param0 order by id";
                var parameterValue = string.Format("%{0}%", terms.First().ToLower());
                results = DataAccessService
                .GetQuery(query)
                .WithParameter("param0", parameterValue)
                .Returning<WebAppItem>()
                .Reading("id", (WebAppItem item, long value) => item.Id = value)
                .Reading("url", (WebAppItem item, string value) =>
                {
                    // Check if there are "{q}" or "%s" for
                    // to be replaced with web search query
                    if (value.Contains("{q}") || value.Contains("%s"))
                    {
                        if (terms.Count() > 1)
                        {
                            var search_string = string.Join("%20", terms.Skip(1).ToArray());
                            value = value.Replace("{q}", search_string).Replace("%s", search_string);
                        }
                    }
                    item.Url = value;
                })
                .Reading("keywords", (WebAppItem item, string value) => item.Keywords = value)
                .Reading("profile", (WebAppItem item, string value) => item.Profile = value)
                .Execute()
                ;
                return results;
            }
            else
            {
                return results;
            }
        }

        public WebAppItem GetItem(string url)
        {
            var query = "select id, url, keywords, profile from webapp_item where url=@url order by id";
            var results = DataAccessService
                .GetQuery(query)
                .WithParameter("url", url)
                .Returning<WebAppItem>()
                .Reading("id", (WebAppItem item, long value) => item.Id = value)
                .Reading("url", (WebAppItem item, string value) => item.Url = value)
                .Reading("keywords", (WebAppItem item, string value) => item.Keywords = value)
                .Reading("profile", (WebAppItem item, string value) => item.Profile = value)
                .Execute()
                ;
            try
            {
                return results.First();
            }
            catch
            {
                return null;
            }

        }

        public void EditWebAppItem(string url, string newUrl, string newKeywords, string newProfile)
        {
            var query = "update webapp_item set url=@url, keywords=@keywords, search=@search, profile=@profile where url=@oldurl";
            DataAccessService
                .GetQuery(query)
                .WithParameter("oldurl", url)
                .WithParameter("url", newUrl)
                .WithParameter("keywords", NormalizeKeywords(newKeywords))
                .WithParameter("search", GetSearchField(newUrl, newKeywords))
                .WithParameter("profile", GetProfile(newProfile))
                .Execute()
            ;
        }
    }
}
