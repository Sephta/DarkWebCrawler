#region Using Statements
// .NET
using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;

// Abot2
using Abot2.Core;
using Abot2.Crawler;
using Abot2.Poco;

// AbotX2
using AbotX2.Core;
using AbotX2.Crawler;  //
using AbotX2.Poco;     //
using AbotX2.Parallel;

// Logger
using Serilog;

//BSON for mongo
using MongoDB.Bson;

//htmlAgilityParser
using HtmlAgilityPack;

// ScrapeAndCrawl
using ScrapeAndCrawl.Extensions;
#endregion


namespace ScrapeAndCrawl
{

    /// <summary>
    /// Utilizing AbotX (for js rendering) this object scrapes specified
    /// websites for keywords, uri links, and other hardcoded data. This
    /// data is then compiled into a data container to interface with.
    /// </summary>
    class DataScraper
    {
        /* ========== Public Members ========= */
        public static List<BsonDocument> dataDocuments = new List<BsonDocument>();
        public static List<string> allParsedText = new List<string>(); 
        public static String siteTitle = "";

#region Public Class Methods
        /* ================================= Class Methods {Public} ============================ */

        /// <summary>
        /// Static method for crawling. Pass in a configuration
        /// (i.e. specify how many sites to crawl, whether or not to 
        /// render js, etc) then creates and executes crawler
        /// </summary>
        public static async Task Crawl(CrawlConfigurationX configX, HttpClientHandler httpHandler, PageHandlerType pageHandlerType, string uriToCrawl = "http://google.com")
        {
            // 'using' sets up scope for crawlerX object to be used
            // disposes of object at end of scope. (i.e. close-curly-brace)
            // I saw this used in the github example. Maybe its good practice??

            ImplementationContainer impContainer = new ImplementationContainer();
            impContainer.PageRequester = new ProxyPageRequester(httpHandler, configX, new WebContentExtractor(), null);

            ImplementationOverride impOverride = new ImplementationOverride(configX, impContainer); 

            using (var crawlerX = new CrawlerX(configX, impOverride))
            {
                crawlerX.ShouldRenderPageJavascript((CrawledPage, CrawlContext) =>
                {
                    if (CrawledPage.Uri.AbsoluteUri.Contains("ghost"))
                        return new CrawlDecision { Allow = false, Reason = "scared to render ghost javascript." };

                    return new CrawlDecision { Allow = true };
                });

                switch (pageHandlerType)
                {
                    case PageHandlerType.wordFrequency:
                        //add handler to be called when the crawl for that page is complete
                        crawlerX.PageCrawlCompleted += WordFrequencyHandler;
                        break;
                    case PageHandlerType.sentimentAnalysis:
                        crawlerX.PageCrawlCompleted += SentimentAnalysisHandler;
                        break;
                }

                await crawlerX.CrawlAsync(new Uri(uriToCrawl));
            }
        }
#endregion

#region Private Class Methods
        /* ================================= Class Methods {Private} =========================== */

        /// <summary>
        /// Handles the PageCrawlCompleted event called by a given Crawler.
        /// This handler parses webpages and collects word frequency data.
        /// </summary>
        private static void WordFrequencyHandler(object sender, PageCrawlCompletedArgs e)
        {
            var httpStatus = e.CrawledPage.HttpResponseMessage.StatusCode;
            var rawPageText = e.CrawledPage.Content.Text;

            // this returns a list of parsed out text content from the raw html
            var parsedText = ParseRawHTML(rawPageText);

            siteTitle = ParseOutWebpageTitle(rawPageText);

            // checks parsedText against list of keywords
            // keywords generated from txt file
            // returns dict of keywords found, how many times found
            var desiredWords = ExcludeWords(parsedText, Constants.DefaultIgnoreWordsTXT);

            // var dict = GetWordCount(desiredWords, Constants.PlaceNamesTXT);
            var dict = GetWordCount(desiredWords);

            // We only want to create and add a bson doc to the list if we
            // actually found some of the data we are looking for
            if (dict.Count > 0)
            {
                var bson = new BsonDocument
                {
                    {"WebsiteTitle", siteTitle},
                    {"URL", e.CrawledPage.Uri.ToString()},
                    {"Raw", rawPageText},
                    {"Locations", new BsonDocument {dict}},
                };

                dataDocuments.Add(bson);
            }
        }

        private static void SentimentAnalysisHandler(object sender, PageCrawlCompletedArgs e)
        {
            CrawledPage crawledPage = e.CrawledPage;

            if (e.CrawledPage.HttpResponseMessage.StatusCode != HttpStatusCode.OK)
            {
                Log.Logger.Debug("Crawl of page failed {0}", crawledPage.Uri.AbsoluteUri);
                return;
            }
            else
                Log.Logger.Debug("Crawl of page succeeded {0}", crawledPage.Uri.AbsoluteUri);

            if (string.IsNullOrEmpty(crawledPage.Content.Text))
            {
                Log.Logger.Debug("Page had no content {0}", crawledPage.Uri.AbsoluteUri);
                return;
            }

            var httpStatus = e.CrawledPage.HttpResponseMessage.StatusCode;
            var rawPageText = e.CrawledPage.Content.Text;

            // this returns a list of parsed out text content from the raw html
            // var parsedText = ParseRawHTML(rawPageText);
            var parsedText = ParseRawHTML_bodyText(rawPageText);

            if (parsedText == null)
            {
                Log.Logger.Debug("WARNING: \"parsedText\" is null after parsing.");
                return;
            }

            allParsedText = allParsedText.Concat(parsedText).ToList();
        }

        /// <summary>
        /// Handles the PageCrawlCompleted event called by a given Crawler.
        /// This handler parses webpages and collects data for rudimentary sentiment analysis.
        /// </summary>
        private static void old_SentimentAnalysisHandler(object sender, PageCrawlCompletedArgs e)
        {
            CrawledPage crawledPage = e.CrawledPage;

            if (e.CrawledPage.HttpResponseMessage.StatusCode != HttpStatusCode.OK)
            {
                Log.Logger.Debug("Crawl of page failed {0}", crawledPage.Uri.AbsoluteUri);
                return;
            }
            else
                Log.Logger.Debug("Crawl of page succeeded {0}", crawledPage.Uri.AbsoluteUri);

            if (string.IsNullOrEmpty(crawledPage.Content.Text))
            {
                Log.Logger.Debug("Page had no content {0}", crawledPage.Uri.AbsoluteUri);
                return;
            }

            var httpStatus = e.CrawledPage.HttpResponseMessage.StatusCode;
            var rawPageText = e.CrawledPage.Content.Text;

            // this returns a list of parsed out text content from the raw html
            // var parsedText = ParseRawHTML(rawPageText);
            var parsedText = ParseRawHTML_bodyText(rawPageText);

            if (parsedText == null)
            {
                Log.Logger.Debug("WARNING: \"parsedText\" is null after parsing.");
                return;
            }

            string siteTitle = ParseOutWebpageTitle(rawPageText);

            // Dictionary containing keywords desired, and a list of all contexts in which they were used
            Dictionary<string, Pair<int, List<string>>> contextCache = GetWordCountAndContext(parsedText, Constants.DefaultIgnoreWordsTXT);

            //make list from dict to sort
            var dictList = contextCache.ToList();

            // Sort takes a comparison operator
            // Comparison(x,y) -> less than 0 if x < y, 0 if equal, greater than 0 if x > y
            // for all keyValuePairs in dict, sort based on the frequency count
            // pair: word : list of 
            dictList.Sort((pair1,pair2) =>  pair1.Value.Item1 > pair2.Value.Item1 ? -1 : 1);

            var sentimentAnalysis = new BsonDocument();

            var numWords = dictList.Count > 50 ? 50 : dictList.Count;
            for (int i = 0; i < numWords; i++)
            {
                //the word we want to check context for
                // Log.Logger.Debug("Getting Context words for " + dictList[i].Key);
                if (dictList[i].Key == "")
                {
                    continue;  // Skips the stupid empty string keyword problem we havn't fixed yet...
                }

                // Excludes words we don't care about
                var desiredWords = ExcludeWords(dictList[i].Value.Item2);

                //the context sentences
                //number of occurances of context words for a given keyword
                var contextWordCount = GetWordCount(desiredWords);

                sentimentAnalysis.Add(new BsonElement(
                    dictList[i].Key,new BsonDocument
                    {
                        {"Count",dictList[i].Value.Item1},
                        {"ContextSentences", new BsonArray(dictList[i].Value.Item2)},
                        {"ContextWordFrequency", new BsonDocument(contextWordCount)}
                    }
                ));
            }

            // BSON doc
            var bson = new BsonDocument
            {
                {"WebsiteTitle", siteTitle},
                {"URL", e.CrawledPage.Uri.ToString()},
                {"Raw", rawPageText},
                {"SentimentAnalysis", sentimentAnalysis}
            };

            if (bson != null)
            {
                dataDocuments.Add(bson);
            }
        }


#region HTML Parsing
        /// <summary>
        /// Parses out text content from raw html using xpath + HTMLAgilityPack
        /// </summary>
        /// <returns>
        /// List of strings, where each string is the text content of a node in the html doc.
        /// Here a node is an html tag. 
        /// </returns>
        private static List<string> ParseRawHTML(string rawHTML)
        {
            List<string> parsed = new List<string>();

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(rawHTML);

            var unwantedNodes = htmlDoc.DocumentNode.SelectNodes("//form");

            if (unwantedNodes != null)
            {
                foreach (var n in unwantedNodes)
                {
                    n.RemoveAllChildren();
                }
            }

            // unwantedNodes.Insert(htmlDoc.DocumentNode.)
            var htmlBody = htmlDoc.DocumentNode.SelectSingleNode("//body");

            if (htmlBody == null)
                return null;

            // Log.Logger.Debug(htmlBody.InnerText);

            foreach (var nNode in htmlBody.Descendants())
            {
                if (nNode.NodeType == HtmlNodeType.Text)
                {
                    if (unwantedNodes == null)
                    {
                        String nodeText = nNode.InnerText;
                        if (nodeText.Any( x => char.IsLetter(x)))
                        {
                            parsed.Add(nNode.InnerText);
                        }
                    }
                    else
                    {
                        if (!unwantedNodes.Contains(nNode))
                        {
                            String nodeText = nNode.InnerText;
                            if (nodeText.Any( x => char.IsLetter(x)))
                            {
                                parsed.Add(nNode.InnerText.ToLower());
                            }   
                        }
                    }         
                }
            }

            return parsed;
        }

        /// <summary>
        /// Pareses the raw html by extracting the body node and spliting
        /// the text at '.' storing the result in a List of strings.
        /// </summary>
        static List<string> ParseRawHTML_bodyText(string rawPageHtml)
        {
            // Load html into document ----------
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(rawPageHtml);
            // ----------------------------------

            var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//body");

            List<string> result = new List<string>(bodyNode.InnerText.Split('.'));

            // Replace '\n' in each line
            for (int i = 0; i < result.Count; i++)
            {
                result[i].Replace("\n", " ");
            }

            return result;
        }

        /// <summary>
        /// Parses out webpage title using xpath + HTMLAgilityPack
        /// </summary>
        /// <returns> String containing the title of a webpage. </returns>
        public static string ParseOutWebpageTitle(string rawPageText)
        {
            string result = "";

            var htmldoc = new HtmlDocument();
            htmldoc.LoadHtml(rawPageText);

            var titlenode = htmldoc.DocumentNode.SelectSingleNode("//title");
            result = titlenode.InnerText;

            return result;
        }
#endregion

        /// <summary>
        /// This is an O(N^2) algorithm for counting number of occurances of certain keywords
        /// </summary>
        /// <returns> Dictionary of (string, int) value pairs </returns>
        public static Dictionary<string,int> GetWordCount(List<string> parsedText, string keywords = null)
        {
            // Create a Hashset of keywords to check against where ...
            // * each key contains only the chars of the keyword
            // * each key is NOT null or empty
            //if type == string (file path) then do : 
             HashSet<string> keywordsSet = new HashSet<string>();
            if (keywords != null){
                keywordsSet = new HashSet<string>(
                File.ReadLines(keywords)
                .Select(keyword => keyword.Trim().ToLower())
                .Where(keyword => !string.IsNullOrEmpty(keyword)),
                StringComparer.OrdinalIgnoreCase
                );

            }
            //else build has

            // Tracks each found word
            HashSet<string> foundWords = new HashSet<string>();

            //  will track number of times the word is found
            Dictionary<string, int> wordInstanceCount = new Dictionary<string, int>();
            List<string> words = new List<string>(); // TODO not used

            for (int i = 0; i < parsedText.Count; i ++)
            {
                foreach(string word in parsedText[i].Split(' '))
                {
                    // Trims out all non letter characters from the word
                    var trimmedWord = word;
                    trimmedWord = new string((
                        from c in trimmedWord
                        where char.IsLetterOrDigit(c)
                        select c
                    ).ToArray());

                    if (keywords == null)
                    {
                        wordInstanceCount[trimmedWord] = wordInstanceCount.ContainsKey(trimmedWord) ? wordInstanceCount[trimmedWord] + 1 : 1;
                    }
                    else
                    { 
                        if (keywordsSet.Contains(trimmedWord))
                        {
                            wordInstanceCount[trimmedWord] = wordInstanceCount.ContainsKey(trimmedWord) ? wordInstanceCount[trimmedWord] + 1 : 1;
                        }
                    }
                }
            }
            return wordInstanceCount;
        }

        /// <summary>
        /// Similar to Get Word Count but also gets the context a word was used in.
        /// </summary>
        public static Dictionary<string, Pair<int, List<string>>> GetWordCountAndContext(List<string> parsedText, string toIgnore, string keywords = null)
        {
            Dictionary<string, Pair<int, List<string>>> result = new Dictionary<string, Pair<int, List<string>>>();

            // Create a Hashset of keywords to check against where ...
            // * each key contains only the chars of the keyword
            // * each key is NOT null or empty
            HashSet<string> keywordsSet = new HashSet<string>();
            if (keywords != null){
                keywordsSet = new HashSet<string>(
                    File.ReadLines(keywords)
                    .Select(keyword => keyword.Trim().ToLower())
                    .Where(keyword => !string.IsNullOrEmpty(keyword) && !string.IsNullOrWhiteSpace(keyword)),
                    StringComparer.OrdinalIgnoreCase
                );

            }

            HashSet<string> ignorewordsSet = new HashSet<string>();
            if (toIgnore != null)
            {
                ignorewordsSet = new HashSet<string>(
                    File.ReadLines(toIgnore)
                    .Select(keyword => keyword.Trim().ToLower())
                    .Where(keyword => !string.IsNullOrEmpty(keyword) && !string.IsNullOrWhiteSpace(keyword)),
                    StringComparer.OrdinalIgnoreCase
                );
            }

            // Tracks each found word
            HashSet<string> foundWords = new HashSet<string>();

            foreach(var str in parsedText)
            {
                foreach(var word in str.Split(' '))
                {

                    // Trims out all non letter characters from the word
                    var trimmedWord = word;
                    trimmedWord = new string((
                        from c in trimmedWord
                        where char.IsLetterOrDigit(c)
                        select c
                    ).ToArray());

                    if (keywords == null)
                    {
                        if (!ignorewordsSet.Contains(trimmedWord) &&
                            trimmedWord.All(Char.IsLetterOrDigit) && 
                            (trimmedWord != "\uFEFF" || trimmedWord != ""))
                        {
                            if (result.ContainsKey(trimmedWord))
                            {
                                result[trimmedWord].Item1++;

                                if (!result[trimmedWord].Item2.Contains(str))
                                    result[trimmedWord].Item2.Add(str);
                            }
                            else if (trimmedWord.All(Char.IsLetterOrDigit))
                            {
                                var newEntry = new Pair<int, List<string>>();

                                newEntry.Item1 = 1;
                                newEntry.Item2 = new List<string>();

                                newEntry.Item2.Add(str);

                                result[trimmedWord] = newEntry;
                            }
                        }
                    }
                    else
                    {
                        if (keywordsSet.Contains(trimmedWord) && 
                            !ignorewordsSet.Contains(trimmedWord) &&
                            trimmedWord.All(Char.IsLetterOrDigit) &&
                            (trimmedWord != "\uFEFF" || trimmedWord != ""))
                        {
                            if (result.ContainsKey(trimmedWord))
                            {
                                result[trimmedWord].Item1++;

                                if (!result[trimmedWord].Item2.Contains(str))
                                    result[trimmedWord].Item2.Add(str);
                            }
                            else if (trimmedWord.All(Char.IsLetterOrDigit))
                            {
                                var newEntry = new Pair<int, List<string>>();

                                newEntry.Item1 = 1;
                                newEntry.Item2 = new List<string>();

                                newEntry.Item2.Add(str);

                                result[trimmedWord] = newEntry;
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary> Used for Word Frequency. This method excludes words we should always ignore. </summary>
        public static List<string> ExcludeWords(List<string> parsedText, string toIgnore=null)
        {   
            // Define a set of words that will be excluded in general
            HashSet<string> ignoredSet = new HashSet<string>(
                File.ReadLines(Constants.DefaultIgnoreWordsTXT)
                .Select(keyword => keyword.Trim().ToLower())
                .Where(keyword => !string.IsNullOrEmpty(keyword)),
                StringComparer.OrdinalIgnoreCase
            );

            // string toIgnore is a file of words that are additive to the set of words already being excluded
            HashSet<string> additiveIgnoredSet = new HashSet<string>();
            if (toIgnore != null)
            {
                additiveIgnoredSet = new HashSet<string>(
                    // TODO maybe have a check if toIgnore is NULL
                    File.ReadLines(toIgnore)
                    .Select(keyword => keyword.Trim().ToLower())
                    .Where(keyword => !string.IsNullOrEmpty(keyword)),
                    StringComparer.OrdinalIgnoreCase
                );
            }

            if (toIgnore != null)
                ignoredSet.UnionWith(additiveIgnoredSet);

            List<string> desiredWords = new List<string>();
            // loop for parsing text
            foreach(var str in parsedText)
            {
                foreach(var word in str.Split(' '))
                {
                    // Trims out all non letter characters from the word
                    var trimmedWord = word;
                    trimmedWord = new string((
                        from c in trimmedWord
                        where char.IsLetterOrDigit(c)
                        select c
                    ).ToArray());
                    trimmedWord.Replace("\uFEFF", "");  // THIS IS TRYING TO GET RID OF zero width no-break space

                    // if word is not contained within general set or within the additive one, might need a check if set is null but not sure
                    if (!ignoredSet.Contains(trimmedWord) && trimmedWord.Any(Char.IsLetter))
                    {
                        desiredWords.Add(trimmedWord);
                    }
                }
            }
            return desiredWords;
        }
#endregion
    }

#region ProxyPageRequester
    /// <summary>
    /// Extend the PageRequester class and override the method that creates the HttpWebRequest
    /// </summary>
    public class ProxyPageRequester : PageRequester
    {
        private readonly CrawlConfiguration _config; 
        private readonly IWebContentExtractor _contentExtractor;
        private readonly CookieContainer _cookieContainer = new CookieContainer();
        private HttpClientHandler _httpClientHandler;
        private HttpClient _httpClient;
        private  HttpClientHandler _torHandler; 

        public ProxyPageRequester(HttpClientHandler torHandler, CrawlConfiguration config, IWebContentExtractor contentExtractor = null, HttpClient httpClient = null) : base(config, contentExtractor, httpClient)
        {
            _config = config;
            _contentExtractor = contentExtractor;

            _torHandler = torHandler;
        }

        /// <summary> Overridden from PageRequester. </summary>
        /// <returns> HttpClientHandler associated with this PageRequester </returns>
        protected override HttpClientHandler BuildHttpClientHandler(Uri rootUri)
        {
            return _torHandler;
        }
    }
#endregion
}

