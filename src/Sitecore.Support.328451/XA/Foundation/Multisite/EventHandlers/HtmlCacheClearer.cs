namespace Sitecore.Support.XA.Foundation.Multisite.EventHandlers
{

    using Caching;
    using Configuration;
    using Data;
    using Data.Items;
    using Diagnostics;
    using Links;
    using Sitecore.Data.Events;
    using Sites;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;
    using Web;

    public class HtmlCacheClearer : Sitecore.XA.Foundation.Multisite.EventHandlers.HtmlCacheClearer
    {
        private readonly string databaseName;

        private readonly IEnumerable<ID> _fieldIds;

        public HtmlCacheClearer()
        {
            XmlNode sourceDatabaseNode = Factory.GetConfigNode("experienceAccelerator/multisite/htmlCacheClearer/sourceDatabase");
            this.databaseName = sourceDatabaseNode.InnerText;

            var xmlNodes = Factory.GetConfigNodes("experienceAccelerator/multisite/htmlCacheClearer/fieldID").Cast<XmlNode>();
            _fieldIds = xmlNodes.Select(node => new ID(node.InnerText));
        }

      public new void OnPublishEndRemote(object sender, EventArgs args)
      {
        Assert.ArgumentNotNull(sender, "sender");
        Assert.ArgumentNotNull(args, "args");
        var sitecoreEventArgs = args as PublishEndRemoteEventArgs;
        if (sitecoreEventArgs != null)
        {
          Database database = Factory.GetDatabase(databaseName, false);
          Item rootItem = database?.GetItem(new ID(sitecoreEventArgs.RootItemId));
          if (rootItem != null)
          {
            List<SiteInfo> sitesToClear = GetUsages(rootItem);
            if (sitesToClear.Count > 0)
            {
              sitesToClear.ForEach(ClearSiteCache);
              return;
            }
          }
        }

        ClearCache(sender, args);
        ClearAllSxaSitesCaches();
      }

      private void ClearSiteCache(string siteName)
        {
            Log.Info(String.Format("HtmlCacheClearer clearing cache for {0} site", siteName), this);
            ProcessSite(siteName);
            Log.Info("HtmlCacheClearer done.", this);
        }


        private void ClearSiteCache(SiteInfo site)
        {
            ClearSiteCache(site.Name);
        }

        private void ProcessSite(string siteName)
        {
            SiteContext site = Factory.GetSite(siteName);
            if (site != null)
            {
                HtmlCache htmlCache = CacheManager.GetHtmlCache(site);
                if (htmlCache != null)
                {
                    htmlCache.Clear();
                }
            }
        }

        private List<SiteInfo> GetUsages(Item item)
        {
            Assert.IsNotNull(item, "item");

            List<SiteInfo> usages = new List<SiteInfo>();
            var currentItem = item;
            do
            {
                var siteItem = MultisiteContext.GetSiteItem(currentItem);
                if (siteItem != null)
                {
                    SiteInfo usage = SiteInfoResolver.GetSiteInfo(currentItem);
                    if (usage != null)
                    {
                        usages.Add(usage);
                        break;
                    }
                }

                ItemLink[] itemReferrers = Globals.LinkDatabase.GetItemReferrers(currentItem, false);
                foreach (ItemLink link in itemReferrers)
                {
                    if (IsOneOfWanted(link.SourceFieldID))
                    {
                        Item sourceItem = link.GetSourceItem();
                        SiteInfo sourceItemSite = SiteInfoResolver.GetSiteInfo(sourceItem);
                        usages.Add(sourceItemSite);
                    }
                }
                currentItem = currentItem.Parent;
            } while (currentItem != null);

            usages = usages.Where(s => s != null).GroupBy(g => new { g.Name }).Select(x => x.First()).ToList();
            usages.AddRange(GetAllSitesForSharedSites(usages));
            return usages;
        }

        private bool IsOneOfWanted(ID sourceFieldId)
        {
            return _fieldIds.Any(x => x.Equals(sourceFieldId));
        }
    }
}