﻿namespace ParTech.Modules.UrlRewriter.Pipelines
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Web;
    using ParTech.Modules.UrlRewriter.Models;
    using Sitecore;
    using Sitecore.Data.Items;
    using Sitecore.Pipelines.HttpRequest;

    //CG - 2014-6-23
    using ParTech.Modules.UrlRewriter.data;
    using Sitecore.Events;
    using ParTech.Modules.UrlRewriter.Events;


    /// <summary>
    /// Pipeline processor that processes URL rewriter rules.
    /// </summary>
    public class UrlRewriteHandler : HttpRequestProcessor
    {
        #region Cache objects

        /// <summary>
        /// Cache for <see cref="UrlRewriteRule"/> objects.
        /// </summary>
        private static List<UrlRewriteRule> urlRewriteRulesCache = new List<UrlRewriteRule>();

        /// <summary>
        /// Cache for <see cref="HostNameRewriteRule"/> objects.
        /// </summary>
        private static List<HostNameRewriteRule> hostNameRewriteRulesCache = new List<HostNameRewriteRule>();

        /// <summary>
        /// Indicates whether the rewrite rules have been loaded from Sitecore.
        /// </summary>
        private static bool rewriteRulesLoaded;

        ///<sumary>
        ///CG. 2014/6/23 this is for loading the rules from XML
        ///</sumary>
        private static RuleException ruleExceptions = new RuleException();

        #endregion

        /// <summary>
        /// Clears the cache so the rewrite rules will be reloaded on the next request.
        /// </summary>
        public static void ClearCache()
        {
            try
            {

                if (urlRewriteRulesCache.Count()>0)
                   urlRewriteRulesCache.Clear();

                if (hostNameRewriteRulesCache.Count()>0)
                   hostNameRewriteRulesCache.Clear();

                rewriteRulesLoaded = false;

                Logging.LogInfo("Cleared rewriter rules cache.", typeof(UrlRewriteHandler));
            }
            catch (Exception ex)
            {
                rewriteRulesLoaded = false;
                Logging.LogError("URL Rewrite - error in the ClearCache: " + ex.Message, typeof(UrlRewriteHandler));
                
            }

        }

        /// <summary>
        /// Executes the pipeline processor.
        /// </summary>
        /// <param name="args"></param>
        public override void Process(HttpRequestArgs args)
        {

          //CG - 2014-07-29. The enabling and disabling the module feature was not program for some reason. Adding what the creator of the module started when having this setting. 
            if (Settings.Enabled)
            { 

            //CG - 2014/6/23 load the xml file
            this.loadRuleExceptions();



            // Ignore requests that are not GET requests,
            // have the context database set to Core or point to ignored sites
            if (this.IgnoreRequest(args.Context))
            {
                return;
            }

            // Load the rewrite rules from Sitecore into the cache.
            this.LoadRewriteRules(args);

            // Rewrite URL's that contain trailing slashes if configuration allows it.
            this.RewriteTrailingSlash(args);

            // Try to tewrite the request URL based on URL rewrite rules.
            this.RewriteUrl(args);

            // Try to rewrite the request URL based on Hostname rewrite rules.
            this.RewriteHostName(args);
            }
           
        }

        #region Rules loading methods

        /// <summary>
        /// Load the rewrite rules from Sitecore.
        /// </summary>
        /// <param name="args">HttpRequest pipeline arguments.</param>
        private void LoadRewriteRules(HttpRequestArgs args)
        {

            try
            {
                if (rewriteRulesLoaded)
                {
                    // Rules are cached and only loaded once when the pipeline processor is called for the first time.
                    // Skip this method if the rules have already been loaded before.
                    return;
                }

                // Verify that we can access the context database.
                if (Context.Database == null)
                {
                    Logging.LogError("Cannot load URL rewrite rules because the Sitecore context database is not set.", this);
                    return;
                }

                // Load the rules folder item from Sitecore and verify that it exists.
                Item rulesFolder = Context.Database.GetItem(Settings.RulesFolderId);

                if ((rulesFolder == null) || (rulesFolder.Axes.GetDescendants().Count()<1))
                {
                    Logging.LogError(string.Format("Cannot load URL rewrite rules folder with ID '{0}' from Sitecore. Verify that it exists.", Settings.RulesFolderId), this);
                    return;
                }

                // Load the rewrite entries and add them to the cache.
                //custom code by CG. for avoiding the rules to load twice 2014-07-30. the condition wrapping the rulesFolder.Axe below

                if ((urlRewriteRulesCache != null) && (urlRewriteRulesCache.Count < rulesFolder.Axes.GetDescendants().Count()))
                {

                    rulesFolder.Axes.GetDescendants()
                        .ToList()
                        .ForEach(this.AddRewriteRule);

                }
                else
                {
                    Logging.LogInfo(string.Format("Avoiding adding the Rules twice because of different threads. The count is {0}", urlRewriteRulesCache.Count), this);
                }
                //end of custom code by CG. 2014-07-30

                // Remember that the rewrite rules are loaded so we don't load them again during the lifecycle of the application.
                rewriteRulesLoaded = true;

                Logging.LogInfo(string.Format("Cached {0} URL rewrite rules and {1} hostname rewrite rules.", urlRewriteRulesCache.Count, hostNameRewriteRulesCache.Count), this);
            }
            catch (Exception ex)
            {
                Logging.LogError("URL Rewrite - error in the LoadRewriteRules: " + ex.Message, typeof(UrlRewriteHandler));
                Event.RaiseEvent("urlrewriter:clearcache", new ClearCacheEventArgs(new ClearCacheEvent()));
            }
        }

        /// <summary>
        /// Add a rewrite rule from Sitecore to the cache.
        /// </summary>
        /// <param name="rewriteRuleItem"></param>
        private void AddRewriteRule(Item rewriteRuleItem)
        {
            // Convert the rewrite rule item to a model object and add to the cache.
            if (rewriteRuleItem.TemplateID.Equals(ItemIds.Templates.UrlRewriteRule))
            {

                
                // Add a URL rewrite rule.
                var rule = new UrlRewriteRule(rewriteRuleItem);

                if (rule.Validate())
                {
                    urlRewriteRulesCache.Add(rule);
                }
            }
            else if (rewriteRuleItem.TemplateID.Equals(ItemIds.Templates.HostNameRewriteRule))
            {
                // Add a hostname rewrite rule.
                var rule = new HostNameRewriteRule(rewriteRuleItem);

                if (rule.Validate())
                {
                    hostNameRewriteRulesCache.Add(rule);
                }
            }
        }

        #endregion

        #region Rewrite methods


        /// <summary>
        /// CG - 2014/06/23. Add this to the Sitecore module to redirect any type that is not control by .net. for example: PDF
        /// When the request comes it turns into a 404 that is handle by the custom code of our sitecore solution. 
        /// Before the code says this is 404 we are intercepting this and checking if is the original URL is a PDF. If it is then the 404 piece is removed (clean-up) so the URL can be use. 
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
       private Uri RewriteForceBasedType(Uri uri)
        {
            try
            {
                if (ruleExceptions.TypeExceptions != null)
                {
                    foreach (var ruleException in ruleExceptions.TypeExceptions)
                    {
                        if (uri.AbsoluteUri.Contains(ruleException.name))
                        {
                            foreach (var subts in ruleException.subTypes)
                            {
                                foreach (var subt in subts.subType)
                                    //if (uri.AbsoluteUri.Contains(subt.subType))
                                    if (uri.AbsoluteUri.Contains(subt))
                                    {
                                        uri = new Uri(uri.Query.Replace("?404;", ""));

                                        string stringRequest = uri.AbsoluteUri.Replace(":" + uri.Port.ToString().Trim(), "");

                                        uri = new Uri(stringRequest);
                                    }
                            }//
                        }
                    }
                }


                /*if (uri.AbsoluteUri.Contains("404") && uri.AbsoluteUri.Contains(".pdf"))
                 {
             
                                
                     uri = new Uri(uri.Query.Replace("?404;", ""));

                     string stringRequest = uri.AbsoluteUri.Replace(":" + uri.Port.ToString().Trim(), "");

                     uri = new Uri(stringRequest);

              
                 }*/
            }
            catch (Exception ex)
            {
                Logging.LogError("URL rewrite - error in RewriteForceBasedType: " + ex.Message, typeof(UrlRewriteHandler));
            }
            return uri;
        }
       /// <summary>
       /// CG - 2014/06/23
       /// </summary>
       private void loadRuleExceptions()
       {

           try
           {
               if (ruleExceptions.TypeExceptions == null)
               {
                   var teo = new DataRepository();

                   ruleExceptions = teo.ruleExceptions;
               }
           }
           catch (Exception ex)
           {
               Logging.LogError("URL rewrite - issue loading the ruleExceptions in the loadRuleExceptions: " + ex.Message, typeof(UrlRewriteHandler));
           }

           

       }

        /// <summary>
        /// If configuration allows it and the request URL ends with a slash, 
        /// the URL is rewritten to one without trailing slash.
        /// </summary>
        /// <param name="args">HttpRequest pipeline arguments.</param>
        private void RewriteTrailingSlash(HttpRequestArgs args)
        {
            string targetUrl = "";
            bool good = true;
            try
            {
                // Only rewrite the URL if configuration allows it.
                if (!Settings.RemoveTrailingSlash)
                {
                    return;
                }

                // Get the request URL and check for a trailing slash.
                Uri requestUrl = args.Context.Request.Url;

                if (requestUrl.AbsolutePath == "/" || !requestUrl.AbsolutePath.EndsWith("/"))
                {
                    // The root document was requested or no trailing slash was found.
                    return;
                }

                // 301-redirect to the same URL, but without trailing slash in the path
                string domain = requestUrl.GetComponents(UriComponents.Scheme | UriComponents.Host, UriFormat.Unescaped);
                string path = requestUrl.AbsolutePath.TrimEnd('/');
                string query = requestUrl.Query;
                targetUrl = string.Concat(domain, path, query);

                if (Settings.LogRewrites)
                {
                    Logging.LogInfo(string.Format("Removed trailing slash from '{0}'.", requestUrl), this);
                }

                //CG rdirects cannot be in try-catch
                // Return a permanent redirect to the target URL.
               // this.Redirect(targetUrl, args.Context);
            }
            catch (Exception ex)
            {
                Logging.LogError("URL rewrite - error in the RewriteTrailingSlash: " + ex.InnerException.Message, "Partech URL Rewrite");
                good = false;
            }
            if (good) { 
            // Return a permanent redirect to the target URL.

            this.Redirect(targetUrl, args.Context);
            }
        }

        /// <summary>
        /// Rewrite the hostname if it matches any of the hostname rewrite rules.
        /// The requested path and querystring is kept intact, only the hostname is rewritten.
        /// </summary>
        /// <param name="args">HttpRequest pipeline arguments.</param>
        private void RewriteHostName(HttpRequestArgs args)
        {
            string targetUrl = "";
            bool good = true;
            try
            {
                if (!hostNameRewriteRulesCache.Any())
                {
                    return;
                }

                // Extract the hostname from the request URL.
                Uri requestUrl = args.Context.Request.Url;
                string hostName = requestUrl.Host;

                // Check if there is a hostname rewrite rule that matches the requested hostname.
                HostNameRewriteRule rule = hostNameRewriteRulesCache
                    .FirstOrDefault(x => x.SourceHostName.Equals(hostName, StringComparison.InvariantCultureIgnoreCase));

                if (rule == null)
                {
                    // No matching rewrite rule was found.
                    return;
                }

                // Set the target URL with the new hostname and the original path and query.
                string scheme = requestUrl.Scheme;
                string path = requestUrl.AbsolutePath;
                string query = requestUrl.Query;

                 targetUrl = string.Concat(scheme, "://", rule.TargetHostName, path, query);

                if (Settings.LogRewrites)
                {
                    // Write an entry to the Sitecore log informing about the rewrite.
                    Logging.LogInfo(string.Format("Hostname rewrite rule '{0}' caused the requested URL '{1}' to be rewritten to '{2}'", rule.ItemId, requestUrl.AbsoluteUri, targetUrl), this);
                }

                //CG redirects cannot be in try-catch
                // Return a permanent redirect to the target URL.
                //this.Redirect(targetUrl, args.Context);
            }
            catch (Exception ex)
            { Logging.LogError("URL rewrite - Error in RewriteHostName: " + ex.Message, typeof(UrlRewriteHandler));
               good = false;
            }

            // Return a permanent redirect to the target URL.
            if (good)
            this.Redirect(targetUrl, args.Context);
        }

        /// <summary>
        /// Rewrite the URL if it matches any of the URL rewrite rules.
        /// </summary>
        /// <param name="args">HttpRequest pipeline arguments.</param>
        private void RewriteUrl(HttpRequestArgs args)
        {
             string targetUrl ="";
            bool good = true;
            try
            {
                if (!urlRewriteRulesCache.Any())
                {
                    return;
                }

                // Prepare flags to retrieve the URL strings from Uri objects.
                var componentsWithoutQuery = UriComponents.Scheme | UriComponents.Host | UriComponents.Path;
                var componentsWithQuery = componentsWithoutQuery | UriComponents.Query;

                Uri requestUrl = args.Context.Request.Url;
                //for 404 caused by a type not handle by .Net - CG
                requestUrl = this.RewriteForceBasedType(requestUrl);

                // If we found a matching URL rewrite rule for the request URL including its querystring,
                // we will rewrite to the exact target URL and dispose the request querystring.
                // Otherwise, if we found a match for the request URL without its querystring,
                // we will rewrite the URL and preserve the querystring from the request.
                bool preserveQueryString = false;

                // Use the request URL including the querystring to find a matching URL rewrite rule.
                UrlRewriteRule rule = urlRewriteRulesCache.FirstOrDefault(x => this.EqualUrl(x.GetSourceUrl(requestUrl), requestUrl, componentsWithQuery));

                if (rule == null)
                {
                    // No match was found, try to find a match for the URL without querystring.
                    rule = urlRewriteRulesCache.FirstOrDefault(x => this.EqualUrl(x.GetSourceUrl(requestUrl), requestUrl, componentsWithoutQuery));

                    preserveQueryString = rule != null;
                }

                if (rule == null)
                {
                    // No matching rewrite rule was found.
                    return;
                }

                // Set the target URL with or without the original request's querystring.
                 targetUrl = preserveQueryString
                    ? string.Concat(rule.GetTargetUrl(requestUrl).GetComponents(componentsWithoutQuery, UriFormat.Unescaped), requestUrl.Query)
                    : rule.GetTargetUrl(requestUrl).GetComponents(componentsWithQuery, UriFormat.Unescaped);

                if (Settings.LogRewrites)
                {
                    // Write an entry to the Sitecore log informing about the rewrite.
                    Logging.LogInfo(string.Format("URL rewrite rule '{0}' caused the requested URL '{1}' to be rewritten to '{2}'", rule.ItemId, requestUrl.AbsoluteUri, targetUrl), "Partech - URL Rewrite");
                }

                //CG this redirect cannot be in as try-catch
                // Return a permanent redirect to the target URL.
                //this.Redirect(targetUrl, args.Context);
            }
            catch (Exception ex)
            {
                //Logging.LogError("URL Rewrite - Error in the RewriteUrl: " + ex.InnerException.Message, "ParTech - URL Rewrite");
                //Logging.LogError("URL Rewrite - Error in the RewriteUrl. attempting to clear cache of module. Error: " + ex.Message, typeof(UrlRewriteHandler));
                //Event.RaiseEvent("urlrewriter:clearcache", new ClearCacheEventArgs(new ClearCacheEvent()));
                Logging.LogInfo("URL Rewrite - Warning in the RewriteUrl. Warning: " + ex.Message, typeof(UrlRewriteHandler));

                if (ex.Message.Contains("Object reference not set to an instance of an object"))
                {
                    Event.RaiseEvent("urlrewriter:clearcache", new ClearCacheEventArgs(new ClearCacheEvent()));
                    good = false;
                }
            }

            // Return a permanent redirect to the target URL.
            if (good)
               this.Redirect(targetUrl, args.Context);
        }

        #endregion

        #region Helper methods

        /// <summary>
        /// Compares the components of two URL's and returns true if they are equal.
        /// </summary>
        /// <param name="a">First URL to compare.</param>
        /// <param name="b">Second URL to compare.</param>
        /// <param name="components">The URI components to compare.</param>
        /// <returns></returns>
        private bool EqualUrl(Uri a, Uri b, UriComponents components)
        {
            string urlA = a.GetComponents(components, UriFormat.Unescaped);
            string urlB = b.GetComponents(components, UriFormat.Unescaped);

            return urlA.Equals(urlB, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// Redirect to the URL using HTTP status code 301 (permanent redirect).
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="httpContext">The HTTP context.</param>
        private void Redirect(string url, HttpContext httpContext)
        {
            if ((url!=null)&& (url.Length>0))
            {
                    if (httpContext == null)
                    {
                        Logging.LogError("Cannot redirect because the HttpContext was not set.", "Partech URL Rewrite");
                        return;
                    }

                    // Return a 301 redirect.
                    httpContext.Response.Clear();
                    httpContext.Response.StatusCode = (int)HttpStatusCode.MovedPermanently;
                    httpContext.Response.RedirectLocation = url;
                    httpContext.Response.End();
            }
        }

        /// <summary>
        /// Indicates whether the current request must be ignored by the URL Rewriter module.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <returns></returns>
        private bool IgnoreRequest(HttpContext httpContext)
        {
            try
            {
                // Only GET request can be rewritten.
                bool getRequest = httpContext.Request.HttpMethod.Equals("get", StringComparison.InvariantCultureIgnoreCase);

                // Check if the context database is set to Core.
                bool coreDatabase = Context.Database != null
                    && Context.Database.Name.Equals(Settings.CoreDatabase, StringComparison.InvariantCultureIgnoreCase);

                // CHeck if the context site is in the list of ignored sites.
                bool ignoredSite = Settings.IgnoreForSites.Contains(Context.GetSiteName().ToLower());

                return !getRequest || coreDatabase || ignoredSite;
            }
            catch (Exception ex)
            {
                Logging.LogError("URL Rewrite - error in the IgnoreRequest: " + ex.InnerException.Message, "Partech URL Rewrite");
                
            }

            return true;
        }

        #endregion
    }
}