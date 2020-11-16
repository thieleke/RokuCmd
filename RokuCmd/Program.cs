using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace RokuCmd
{
    //  https://developer.roku.com/docs/developer-program/debugging/external-control-api.md
    class Program
    {
        public static string RokuIP = "10.0.0.131";
        static void Main(string[] args)
        {
            if(args.Length == 0)
            {
                Console.WriteLine("Usage: rokucmd <YouTube URL, netflix|pluto|shout|youtube, mute (.seconds or minutes), status, pause (.seconds or minutes), Key value>");
                return;
            }

            string arg_lower = args[0].ToLower();

            if(GetPowerStatus() == false)
            {
                if(SetPower(true) == false)
                {
                    Console.WriteLine("Unable to turn on Roku");
                    return;
                }
            }

            try
            {
                string contentId = String.Empty;
                Uri ytUri = new Uri(args[0]);
                if(Regex.IsMatch(ytUri.Host, "^(m.|www.)?youtube.com$", RegexOptions.IgnoreCase) == true)
                {
                    NameValueCollection queryParams = System.Web.HttpUtility.ParseQueryString(ytUri.Query);
                    contentId = queryParams["v"];
                }
                else if(ytUri.Host.ToLower() == "youtu.be")
                {
                    contentId = ytUri.AbsolutePath;
                }

                if(String.IsNullOrWhiteSpace(contentId) == false)
                {
                    Console.WriteLine($"Launching YouTube {ytUri}");
                    LaunchContentId(AppId.YouTube, contentId, true);
                    return;
                }
            }
            catch
            {
            }

            if(arg_lower == "launch")
            {
                if(args.Length > 1)
                {
                    if(args.Length > 2)
                    {
                        LaunchContentId(args[1].Trim(), args[2].Trim());
                    }
                    else
                    {
                        LaunchContentId(args[1].Trim());
                    }
                }

                return;
            }
                
            if(arg_lower == "shout")
            {
                Console.WriteLine("Launching Shout Factory TV");
                LaunchShoutFactory();
                return;
            }

            if(arg_lower == "pluto")
            {
                LaunchContentId(AppId.Pluto);
                return;
            }

            if(arg_lower == "youtube")
            {
                LaunchContentId(AppId.YouTube);
                return;
            }

            if(arg_lower == "netflix")
            {
                LaunchContentId(AppId.Netflix);
                return;
            }

            if(arg_lower == "mute")
            {
                if(args.Length > 1)
                {
                    Mute(args[1]);
                    return;
                }
                else
                {
                    SendKeypress(Keypress.VolumeMute);
                    return;
                }
            }

            if(arg_lower == "status")
            {
                Console.WriteLine(GetStatus());
                return;
            }

            if(arg_lower == "pause" && args.Length > 1)
            {
                Pause(args[1]);
                return;
            }

            // Fallthrough
            SendKeypress(args[0]);
        }

        public static bool Pause(string value, string msgPrefix = "Pausing")
        {
            bool doMinutes = true;
            if(value.StartsWith("."))
            {
                value = value.Replace(".", "");
                doMinutes = false;
            }

            if(Int32.TryParse(value, out int val) == false)
            {
                return false;
            }

            string units = doMinutes ? "minute" : "second";

            if(val == 1)
            {
                Console.WriteLine($"{msgPrefix} for {val} {units}");
            }
            else
            {
                Console.WriteLine($"{msgPrefix} for {val} {units}s");
            }

            if(doMinutes)
            {
                val *= 60;
            }

            Sleep(val);
            return true;
        }

        public static void Mute(string value=null)
        {
            SendKeypress(Keypress.VolumeMute);
            if(String.IsNullOrWhiteSpace(value))
            {                
                return;
            }

            if(Pause(value, "Muting"))
            {
                SendKeypress(Keypress.VolumeMute);
            }
        }

        static Uri GetRokuUri(string path = null, List<Tuple<string, string>> queryParams = null)
        {
            UriBuilder uriBuilder = new UriBuilder { Scheme = "http", Host = RokuIP, Port = 8060 };
            if(String.IsNullOrWhiteSpace(path) == false)
                uriBuilder.Path = path;
            if(queryParams != null)
            {
                string query = String.Empty;
                foreach(var tuple in queryParams)
                {
                    query += $"{WebUtility.UrlEncode(tuple.Item1)}={WebUtility.UrlEncode(tuple.Item2)}";
                }
                uriBuilder.Query = query;
            }

            return uriBuilder.Uri;
        }

        static void PostUri(string path, List<Tuple<string, string>> queryParams = null)
        {
            Uri uri = GetRokuUri(path, queryParams);
            var task = Task.Run(() => new HttpClient().PostAsync(uri, null));
            task.Wait();
        }
        static void SendKeypress(string key)
        {
            string keypress = Keypress.GetKeypress(key);
            if(String.IsNullOrWhiteSpace(keypress) == false)
            {
                Console.WriteLine($"Keypress {keypress}");
                PostUri($"/keypress/{keypress}");
            }
        }

        static void Sleep(int seconds)
        {
            System.Threading.Thread.Sleep(seconds * 1000);
        }

        static bool GetPowerStatus()
        {
            try
            {
                Uri uri = GetRokuUri("/query/device-info");
                var task = Task.Run(() => new HttpClient().GetStringAsync(uri));
                task.Wait();
                string payload = task.Result;
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(payload);

                // <power-mode>DisplayOff</power-mode>
                // <power-mode>PowerOn</power-mode>
                return doc.SelectSingleNode("//device-info/power-mode").InnerText.ToLower() == "poweron";
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Error in GetPowerStatus(): {ex}");
                throw;
            }
        }

        static bool SetPower(bool powerOn)
        {
            SendKeypress(powerOn ? Keypress.PowerOn : Keypress.PowerOff);
            return GetPowerStatus();
        }

        public static bool IsLaunched(string appId)
        {
            // http://10.0.0.131:8060/query/media-player
            /* <player error="false" state="play">
            <plugin bandwidth="68436193 bps" id="53696" name="Shout! Factory TV"/>
            */

            Uri uri = GetRokuUri("/query/media-player");
            var task = Task.Run(() => new HttpClient().GetStringAsync(uri));
            task.Wait();
            string payload = task.Result;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(payload);
            XmlNode node = doc.SelectSingleNode($"//player/plugin[@id='{appId}']");
            Console.WriteLine($"{appId} launched = {node != null}");
            return node != null;
        }

        public static string GetStatus()
        {
            Uri uri = GetRokuUri("/query/media-player");
            var task = Task.Run(() => new HttpClient().GetStringAsync(uri));
            task.Wait();
            string payload = task.Result;
            return payload;
        }
        public static string GetState()
        {
            // http://10.0.0.131:8060/query/media-player
            /* <player error="false" state="stop">
                <plugin bandwidth="56592555 bps" id="837" name="YouTube"/>
                <format audio="opus" captions="none" drm="none" video="vp9"/>
                <is_live>false</is_live>
               </player>
            */

            string payload = GetStatus();
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(payload);
            XmlNode node = doc.SelectSingleNode($"//player");
            return node != null ? node.Attributes["state"].Value : "";
        }

        public static void LaunchContentId(string appId, string contentId="", bool forceLaunch=true, bool wait=true)
        {
            // http://10.0.0.131:8060/launch/837?contentId=gUAj7UdDqfI"
            bool isLaunched = !forceLaunch && IsLaunched(appId);
            string launchStr = isLaunched ? "input" : "launch";
            Console.WriteLine($"{launchStr} {appId}");
            PostUri($"/{launchStr}/{appId}", new List<Tuple<string, string>> { new Tuple<string, string>("contentId", contentId) });

            for(int i = 0; i < 60; i++)
            {
                if(wait == false || IsLaunched(appId))
                    return;

                Sleep(1);
            }
        }
        public static void LaunchShoutFactory()
        {
            if(IsLaunched(AppId.ShoutFactory))
            {
                SendKeypress(Keypress.Home);
                Sleep(1);
            }

            LaunchContentId(AppId.ShoutFactory);
            Sleep(15);
            SendKeypress(Keypress.Down);
            Sleep(1);
            SendKeypress(Keypress.Right);
            Sleep(1);
            SendKeypress(Keypress.Select);
            Sleep(1);
            SendKeypress(Keypress.Select);
        }
    }

    public static class AppId
    {
        public const string YouTube = "837";
        public const string ShoutFactory = "53696";
        public const string Pluto = "74519";
        public const string Netflix = "12";
    }
    public static class Keypress
    {
        public const string Home = "Home";
        public const string Rev = "Rev";
        public const string Fwd = "Fwd";
        public const string Play = "Play";
        public const string Select = "Select";
        public const string Left = "Left";
        public const string Right = "Right";
        public const string Down = "Down";
        public const string Up = "Up";
        public const string Back = "Back";
        public const string InstantReplay = "InstantReplay";
        public const string Info = "Info";
        public const string Backspace = "Backspace";
        public const string Search = "Search";
        public const string Enter = "Enter";
        public const string VolumeDown = "VolumeDown";
        public const string VolumeMute = "VolumeMute";
        public const string VolumeUp = "VolumeUp";
        public const string PowerOff = "PowerOff";
        public const string PowerOn = "PowerOn";
        public const string ChannelUp = "ChannelUp";
        public const string ChannelDown = "ChannelDown";
        public const string InputTuner = "InputTuner";
        public const string InputHDMI1 = "InputHDMI1";
        public const string InputHDMI2 = "InputHDMI2";
        public const string InputHDMI3 = "InputHDMI3";
        public const string InputHDMI4 = "InputHDMI4";
        public const string InputAV1 = "InputAV1";

        static readonly Dictionary<string, string> LowerMapping = new Dictionary<string, string>
        {
            { "home", Home },
            { "rev", Rev },
            { "fwd", Fwd },
            { "play", Play },
            { "select", Select },
            { "left", Left },
            { "right", Right },
            { "down", Down },
            { "up", Up },
            { "back", Back },
            { "instantreplay", InstantReplay },
            { "replay", InstantReplay },
            { "info", Info },
            { "backspace", Backspace },
            { "search", Search },
            { "enter", Enter },
            { "volumndown", VolumeDown },
            { "vdown", VolumeDown },
            { "volumeup", VolumeUp },
            { "vup", VolumeUp },
            { "volumemute", VolumeMute },
            { "mute", VolumeMute },
            { "poweron", PowerOn },
            { "on", PowerOn },
            { "poweroff", PowerOff },
            { "off", PowerOff },
            { "channelup", ChannelUp },
            { "channeldown", ChannelDown },
            { "inputtuner", InputTuner },
            { "inputhdmi1", InputHDMI1 },
            { "inputhdmi2", InputHDMI2 },
            { "inputhdmi3", InputHDMI3 },
            { "inputhdmi4", InputHDMI4 },
            { "inputav1", InputAV1 }
        };

        public static string GetKeypress(string key)
        {
            string result = String.Empty;

            if(String.IsNullOrWhiteSpace(key) == false)
            {
                if(Regex.IsMatch(key, @"^[\w]$", RegexOptions.IgnoreCase))
                {
                    result = "Lit_" + key;
                }
                else
                {
                    LowerMapping.TryGetValue(key.ToLower().Trim(), out result);
                }
            }

            return result;
        }
    }
}
