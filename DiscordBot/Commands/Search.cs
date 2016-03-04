﻿using Discord;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DiscordBot.Commands
{
    class Search
    {
        public static async void Ask(object s, MessageEventArgs e)
        {
            try
            {
                JObject Ask = null;
                string Type = "Neutral";
                while (Type == "Neutral")
                {
                    var reqString = "https://8ball.delegator.com/magic/JSON/" + Uri.EscapeUriString((string)s);
                    Ask = JObject.Parse(await reqString.ResponseAsync());
                    Type = Ask["magic"]["type"].ToString();
                }

                Bot.Send(e.Channel, e.User.Mention + " " + Ask["magic"]["answer"].ToString());
            }
            catch (Exception Ex)
            {
                $"Ask: {Ex}".Log();
            }
        }

        public static async void Youtube(object s, MessageEventArgs e)
        {
            try
            {
                string Url = await Search.YoutubeResult((string)s);
                if (Url != String.Empty)
                {
                    Bot.Send(e.Channel, "I think I found it.. " + Url);
                    return;
                }
            }
            catch (Exception Ex)
            {
                $"YtSearch {Ex}".Log();
            }

            Bot.Send(e.Channel, e.User.Mention + " " + Conversation.CantFind);
        }

        public static async void Image(object s, MessageEventArgs e)
        {
            try
            {
                string Req = "https://www.googleapis.com/customsearch/v1?q=" + Uri.EscapeDataString((string)s) + "&cx=018084019232060951019%3Ahs5piey28-e&num=1&searchType=image&start=" + new Random().Next(1, 15) + "&fields=items%2Flink&key=" + Bot.GoogleAPI;
                JObject obj = JObject.Parse(await Req.ResponseAsync());
                Bot.Send(e.Channel, obj["items"][0]["link"].ToString());
            }
            catch
            {
                Bot.Send(e.Channel, e.User.Mention + " " + Conversation.CantFind);
            }
        }

        public static void Osu(object s, MessageEventArgs e)
        {
            using (System.Net.WebClient cl = new System.Net.WebClient())
            {
                cl.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                cl.Headers.Add(System.Net.HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 6.2; Win64; x64)");
                cl.DownloadDataAsync(new Uri("http://lemmmy.pw/osusig/sig.php?uname=" + (string)s + "&flagshadow&xpbar&xpbarhex&pp=2"));
                cl.DownloadDataCompleted += (sender, cle) => {
                    Bot.Send(e.Channel, (string)s + ".png", new MemoryStream(cle.Result));
                    Bot.Send(e.Channel, "Profile Link: https://osu.ppy.sh/u/" + Uri.EscapeDataString((string)s));
                };
            }
        }

        public static async void Avatar(object s, MessageEventArgs e)
        {
            if (e.Message.MentionedUsers.Count() > 0)
            {
                Bot.Send(e.Channel, await e.Message.MentionedUsers.First().AvatarUrl.ShortUrl());
            }
        }

        public static void Define(object s, MessageEventArgs e)
        {
            string Query = (string)s;
            if (Query.StartsWith("a "))
            {
                Query = Query.Substring(2);
            }
            else if (Query.StartsWith("an "))
            {
                Query = Query.Substring(3);
            }

            Search.DefineSimple(Query, e);
        }

        public static async void DefineSimple(object s, MessageEventArgs e)
        {
            try
            {
                string Query = (string)s;

                if (Query != String.Empty)
                {
                    WebHeaderCollection Headers = new WebHeaderCollection();
                    Headers.Add("X-Mashape-Key", Bot.MashapeAPI);
                    JObject Json = JObject.Parse(await ($"https://mashape-community-urban-dictionary.p.mashape.com/define?term=" + Uri.EscapeUriString(Query)).ResponseAsync(Headers));
                    Bot.Send(e.Channel, Json["list"][0]["definition"].ToString());
                }
            }
            catch
            {
                Bot.Send(e.Channel, "I have no idea");
            }
        }

        public static async void Lewd(object s, MessageEventArgs e)
        {
            string Query = (string)s;
            var RNG = new Random();

            try
            {
                if (Query == "loli")
                {
                    Query = "flat_chest";
                }

                MatchCollection Matches = Regex.Matches(await ("http://danbooru.donmai.us/posts?page=" + RNG.Next(0, 15) + "&tags=" + Query.Replace(" ", "_")).ResponseAsync(), "data-large-file-url=\"(?<id>.*?)\"");
                if (Matches.Count > 0)
                {
                    Bot.Send(e.Channel, await ("http://danbooru.donmai.us" + Matches[RNG.Next(0, Matches.Count)].Groups["id"].Value).ShortUrl());
                    return;
                }

                Matches = Regex.Matches(await ("http://gelbooru.com/index.php?page=post&s=list&pid=" + RNG.Next(0, 10) * 42 + "&tags=" + Query.Replace(" ", "_")).ResponseAsync(), "span id=\"s(?<id>\\d*)\"");
                if (Matches.Count > 0)
                {
                    Bot.Send(e.Channel, await (Regex.Match(await ("http://gelbooru.com/index.php?page=post&s=view&id=" + Matches[RNG.Next(0, Matches.Count)].Groups["id"].Value).ResponseAsync(), "\"(?<url>http://simg4.gelbooru.com//images.*?)\"").Groups["url"].Value).ShortUrl());
                    return;
                }
            }
            catch (Exception Ex)
            {
                Bot.Client.Log.Log(LogSeverity.Error, "Lewd Search", null, Ex);
            }

            Bot.Send(e.Channel, "I couldn't find anything");
        }

        private static string AniToken = null;

        public static void AnimeInfo(string s, MessageEventArgs e)
        {
            RestClient API = Search.GetAniApi();

            RestRequest SearchRequest = new RestRequest("/anime/search/" + Uri.EscapeUriString(s));
            SearchRequest.AddParameter("access_token", Search.AniToken);
            string SearchResString = API.Execute(SearchRequest).Content;

            if (SearchResString.Trim() != String.Empty && JToken.Parse(SearchResString) is JArray)
            {
                RestRequest InfoRequest = new RestRequest("/anime/" + JArray.Parse(SearchResString)[0]["id"]);
                InfoRequest.AddParameter("access_token", Search.AniToken);

                JObject Info = JObject.Parse(API.Execute(InfoRequest).Content);

                string Title = "`" + Info["title_romaji"] + "`";
                if (Title != Info["title_english"].ToString())
                {
                    Title += " / `" + Info["title_english"] + "`";
                }

                string Extra = "";
                if (Info["total_episodes"].ToString() != "0" && Info["average_score"].ToString() != "0")
                {
                    Extra = Info["total_episodes"] + " Episodes (" + Info["airing_status"] + ") - Scored " + Info["average_score"] + "\n";
                }

                Bot.Send(e.Channel, Title + "\n" + Extra +
                    "Synopsis: " + WebUtility.HtmlDecode(Info["description"].ToString()).Replace("<br>", "\n").Compact(250) + "\n" +
                    "More info at http://anilist.co/anime/" + Info["id"] + "\n" + Info["image_url_lge"]);
            }
            else
            {
                Bot.Send(e.Channel, e.User.Mention + " " + Conversation.CantFind);
            }
        }

        public static void MangaInfo(string s, MessageEventArgs e)
        {
            RestClient API = Search.GetAniApi();

            RestRequest SearchRequest = new RestRequest("/manga/search/" + Uri.EscapeUriString(s));
            SearchRequest.AddParameter("access_token", Search.AniToken);
            string SearchResString = API.Execute(SearchRequest).Content;

            if (SearchResString.Trim() != String.Empty && JToken.Parse(SearchResString) is JArray)
            {
                RestRequest InfoRequest = new RestRequest("/manga/" + JArray.Parse(SearchResString)[0]["id"]);
                InfoRequest.AddParameter("access_token", Search.AniToken);

                JObject Info = JObject.Parse(API.Execute(InfoRequest).Content);

                string Title = "`" + Info["title_romaji"] + "`";
                if (Title != Info["title_english"].ToString())
                {
                    Title += " / `" + Info["title_english"] + "`";
                }

                string Extra = "";
                if (Info["total_chapters"].ToString() != "0" && Info["average_score"].ToString() != "0")
                {
                    Extra = Info["total_chapters"] + " Chapters (" + Info["publishing_status"] + ") - Scored " + Info["average_score"] + "\n";
                }

                Bot.Send(e.Channel, Title + "\n" + Extra +
                    "Synopsis: " + WebUtility.HtmlDecode(Info["description"].ToString()).Replace("<br>", "\n").Compact(250) + "\n" +
                    "More info at http://anilist.co/manga/" + Info["id"] + "\n" + Info["image_url_lge"]);
            }
            else
            {
                Bot.Send(e.Channel, e.User.Mention + " " + Conversation.CantFind);
            }
        }

        private static RestClient GetAniApi()
        {
            RestClient API = new RestClient("http://anilist.co/api");

            RestRequest TokenRequest = new RestRequest("/auth/access_token", RestSharp.Method.POST);
            TokenRequest.AddParameter("grant_type", "client_credentials");
            TokenRequest.AddParameter("client_id", Bot.AniIdAPI);
            TokenRequest.AddParameter("client_secret", Bot.AniSecretAPI);
            AniToken = JObject.Parse(API.Execute(TokenRequest).Content)["access_token"].ToString();

            return API;
        }

        //Made by owner of Nadekobot
        public static async Task<string> YoutubeResult(string Query)
        {
            try
            {
                //maybe it is already a youtube url, in which case we will just extract the id and prepend it with youtube.com?v=
                Match Match = new Regex("(?:youtu\\.be\\/|v=)(?<id>[\\da-zA-Z\\-_]*)").Match(Query);
                if (Match.Length > 1)
                {
                    return "http://www.youtube.com?v=" + Match.Groups["id"].Value;
                }

                WebRequest wr = WebRequest.Create("https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=1&q=" + Uri.EscapeDataString(Query) + "&key=" + Bot.GoogleAPI);
                StreamReader sr = new StreamReader((await wr.GetResponseAsync()).GetResponseStream());

                dynamic obj = JObject.Parse(await sr.ReadToEndAsync());
                return "http://www.youtube.com/watch?v=" + obj.items[0].id.videoId.ToString();
            }
            catch (Exception Ex)
            {
                $"YtResult {Ex}".Log();
            }

            return string.Empty;
        }
    }
}