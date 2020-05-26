using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyMute
{
    public enum State
    {
        _1_Idle,
        _2_PromptForCode,
        _3_RequestAccessKey,
        _4_RefreshAccessKey,
        _5_GetCurrentSong
    }

    public class Requester : IDisposable
    {
        const string GET_SONG = "https://api.spotify.com/v1/me/player/currently-playing";
        const string GET_BEARER = "https://accounts.spotify.com/api/token";
        const string GET_CODE = "https://accounts.spotify.com/authorize";

        const string APP_CLIENT_ID = "8a2e15bc96784c92beab28bc6193e063";
        const string APP_CLIENT_SECRET_KEY = "0636539a14a843c69b0c32bcfdf75c0a";
        const string REDIRECT_URL = "http://127.0.0.1:666";

        public event Action<State> StateChanged;

        string m_Code;
        string m_RefreshToken;
        string m_AccessToken;

        State m_State = SpotifyMute.State._1_Idle;

        State State
        {
            get
            {
                return m_State;
            }
            set
            {
                if (m_State != value)
                {
                    m_State = value;
                    Observable.Start(() => StateChanged?.Invoke(m_State));
                }
            }
        }
        Thread m_Thread;
        IntPtr m_Handle;
        bool m_Disposed = false;

        public Requester(IntPtr handle)
        {
            m_Handle = handle;
            m_Thread = new Thread(Loop);
            m_Thread.Start();
        }

        void Loop()
        {
            while (true)
            {
                switch (State)
                {
                    case State._1_Idle:
                        break;
                    case State._2_PromptForCode:
                        PromptForCode();
                        break;
                    case State._3_RequestAccessKey:
                        GetAccessToken();
                        break;
                    case State._4_RefreshAccessKey:
                        RefreshAccessToken();
                        break;
                    case State._5_GetCurrentSong:
                        MuteIfAids();
                        break;
                }

                if (m_Disposed)
                    return;

                System.Threading.Thread.Sleep(1000);
            }
        }

        #region Get Code

        const string CODE_PARAM_CLIENT_ID = "client_id";
        const string CODE_PARAM_RESPONSE_TYPE = "response_type";
        const string CODE_PARAM_RESPONSE_TYPE_VALUE = "code";
        const string CODE_PARAM_REDIRECT_URL = "redirect_uri";
        const string CODE_PARAM_SCOPE = "scope";

        const string CODE_PARAM_SCOPE_VALUE = "user-read-currently-playing";

        public void KickOff()
        {
            if (State == State._1_Idle)
            {
                State = State._2_PromptForCode;
            }
        }

        private void PromptForCode()
        {
            try
            {
                string PrompthForCode = $@"{GET_CODE}?{CODE_PARAM_CLIENT_ID}={APP_CLIENT_ID}&{CODE_PARAM_RESPONSE_TYPE}={CODE_PARAM_RESPONSE_TYPE_VALUE}&{CODE_PARAM_REDIRECT_URL}={REDIRECT_URL}&" +
                    $"{CODE_PARAM_SCOPE}={CODE_PARAM_SCOPE_VALUE}";

                HttpListener listener = new HttpListener();
                listener.Prefixes.Add(REDIRECT_URL + "/");

                System.Diagnostics.Process.Start(PrompthForCode);
                listener.Start();
                var context = listener.GetContext();
                var request = context.Request;
                var response = context.Response;

                string responseString = "<!DOCTYPE html><html><head><style>body {    background-image: url(\"https://vignette.wikia.nocookie.net/legomessageboards/images/5/5f/Pacman.gif\");    background-repeat: y;}</style></head><body></body></html>";
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);

                response.ContentLength64 = buffer.Length;
                Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);

                output.Close();
                listener.Stop();

                if (request.RawUrl.StartsWith("/?code="))
                {
                    m_Code = request.RawUrl.Substring(7);
                    State = SpotifyMute.State._3_RequestAccessKey;
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine("Failed to get code");
                    System.Diagnostics.Trace.WriteLine(request.RawUrl);
                    State = State._1_Idle;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.ToString());
                State = State._1_Idle;
            }
        }

        #endregion

        #region Get token

        private void GetAccessToken()
        {
            string data = "";
            try
            {
                Dictionary<string, string> parameters = new Dictionary<string, string>()
                {
                    { "grant_type",    "authorization_code" },
                    { "code",          m_Code },
                    { "redirect_uri",  REDIRECT_URL },
                    { "client_id",     APP_CLIENT_ID },
                    { "client_secret", APP_CLIENT_SECRET_KEY}
                };

                using (var httpClient = new HttpClient())
                {
                    using (var content = new FormUrlEncodedContent(parameters.ToArray()))
                    {
                        content.Headers.Clear();
                        content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                        HttpResponseMessage response = httpClient.PostAsync(GET_BEARER, content).Result;

                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            data = response.Content.ReadAsStringAsync().Result;

                            JObject obj = JsonConvert.DeserializeObject(data) as JObject;

                            var access = obj.Value<string>("access_token");
                            var refresh = obj.Value<string>("refresh_token");

                            if (!string.IsNullOrEmpty(access) && !string.IsNullOrEmpty(refresh))
                            {
                                m_AccessToken = access;
                                m_RefreshToken = refresh;
                                State = State._5_GetCurrentSong;
                            }
                            else
                            {
                                System.Diagnostics.Trace.WriteLine("Failed to get access token");
                                System.Diagnostics.Trace.WriteLine(data);
                                State = State._1_Idle;
                            }
                        }
                        else
                        {
                            System.Diagnostics.Trace.WriteLine("Failed to get access token");
                            System.Diagnostics.Trace.WriteLine("Data : " + data);
                            State = State._1_Idle;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex.ToString());
                System.Diagnostics.Trace.WriteLine("Data : " + data);
                State = State._1_Idle;
            }
        }

        #endregion

        #region Get Current song

        bool m_MutedByMe = false;

        private void MuteIfAids()
        {
            string data = "";
            try
            {
                using (var httpClient = new HttpClient())
                {
                    HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, GET_SONG);
                    message.Headers.Add("Authorization", $"Bearer {m_AccessToken}");

                    var response = httpClient.SendAsync(message).Result;
                    data = response.Content.ReadAsStringAsync().Result;

                    if (!string.IsNullOrEmpty(data))
                    {
                        JObject obj = JsonConvert.DeserializeObject(data) as JObject;

                        Object item = obj.Value<JObject>("item");
                        string isPlaying = obj.Value<string>("is_playing");
                        string offset = obj.Value<string>("progress_ms");
                        JObject error = obj.Value<JObject>("error");

                        if (!string.IsNullOrEmpty(isPlaying) && !string.IsNullOrEmpty(offset))
                        {
                            if (item == null && isPlaying == "True" && offset != "0")
                            {
                                if (!m_MutedByMe)
                                {
                                    SystemAudio.SetMute(true);
                                    m_MutedByMe = true;
                                }
                            }
                            else
                            {
                                if (m_MutedByMe)
                                {
                                    SystemAudio.SetMute(false);
                                    m_MutedByMe = false;
                                }
                            }
                        }
                        else
                        {
                            if (error != null)
                            {
                                string error_message = error.Value<string>("message");

                                if (error_message == "The access token expired")
                                {
                                    System.Diagnostics.Trace.WriteLine("Token expired");
                                    State = State._4_RefreshAccessKey;
                                }
                            }
                            else
                            {
                                System.Diagnostics.Trace.WriteLine("Failed to get current song");
                                System.Diagnostics.Trace.WriteLine("Data : " + data);
                                State = State._1_Idle;
                            }
                        }
                    }
                    else
                    {
                        // not listening to music right now
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("Failed to get current song");
                System.Diagnostics.Trace.WriteLine(ex.ToString());

                if (!string.IsNullOrEmpty(data))
                {
                    if (data.Contains("503 Service Unavailable"))
                    {
                        System.Diagnostics.Trace.WriteLine("503 Service Unavailable");
                        System.Diagnostics.Trace.WriteLine("Waiting 5 minutes and resuming");

                        System.Threading.Thread.Sleep((int)TimeSpan.FromMinutes(5).TotalMilliseconds);

                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine("Data : " + data);
                        State = State._1_Idle;
                    }
                }
                else
                {
                    State = State._1_Idle;
                }
            }
        }

        public void Dispose()
        {
            m_Disposed = true;
        }

        #endregion

        #region Refresh Access Key

        private void RefreshAccessToken()
        {
            string data = "";
            try
            {
                Dictionary<string, string> parameters = new Dictionary<string, string>()
                {
                    { "grant_type",    "refresh_token" },
                    { "refresh_token", m_RefreshToken },
                    { "client_id",     APP_CLIENT_ID },
                    { "client_secret", APP_CLIENT_SECRET_KEY}
                };

                using (var httpClient = new HttpClient())
                {
                    using (var content = new FormUrlEncodedContent(parameters.ToArray()))
                    {
                        content.Headers.Clear();
                        content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                        HttpResponseMessage response = httpClient.PostAsync(GET_BEARER, content).Result;

                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            data = response.Content.ReadAsStringAsync().Result;

                            JObject obj = JsonConvert.DeserializeObject(data) as JObject;

                            var access = obj.Value<string>("access_token");

                            if (!string.IsNullOrEmpty(access))
                            {
                                m_AccessToken = access;
                                State = State._5_GetCurrentSong;
                            }
                            else
                            {
                                System.Diagnostics.Trace.WriteLine("Failed to refresh access token");
                                System.Diagnostics.Trace.WriteLine("Data : " + data);
                                State = State._1_Idle;
                            }
                        }
                        else
                        {
                            System.Diagnostics.Trace.WriteLine("Failed to refresh access token");
                            System.Diagnostics.Trace.WriteLine("Data : " + data);
                            State = State._1_Idle;
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("Failed to refresh access token");
                System.Diagnostics.Trace.WriteLine(ex.ToString());
                System.Diagnostics.Trace.WriteLine("Data : " + data);
                State = State._1_Idle;
            }
        }

        #endregion

    }
}
