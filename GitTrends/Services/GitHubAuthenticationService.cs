﻿using System;
using System.Threading.Tasks;
using System.Web;
using AsyncAwaitBestPractices;
using GitTrends.Shared;
using Newtonsoft.Json;
using Xamarin.Essentials;

namespace GitTrends
{
    public static class GitHubAuthenticationService
    {
        #region Constant Fields
        const string _oauthTokenKey = "OAuthToken";
        readonly static WeakEventManager<AuthorizeSessionCompletedEventArgs> _authorizeSessionCompletedEventManager = new WeakEventManager<AuthorizeSessionCompletedEventArgs>();
        #endregion

        #region Fields
        static string _sessionId;
        #endregion

        #region Events
        public static event EventHandler<AuthorizeSessionCompletedEventArgs> AuthorizeSessionCompleted
        {
            add => _authorizeSessionCompletedEventManager.AddEventHandler(value);
            remove => _authorizeSessionCompletedEventManager.RemoveEventHandler(value);
        }
        #endregion

        #region Properties
        public static string Alias
        {
            get => Preferences.Get(nameof(Alias), string.Empty);
            set => Preferences.Set(nameof(Alias), value);
        }
        #endregion

        #region Methods
        public static async Task<string> GetGitHubLoginUrl()
        {
            _sessionId = Guid.NewGuid().ToString();

            var clientIdDTO = await AzureFunctionsApiService.GetGitHubClientId().ConfigureAwait(false);

            return $"{GitHubConstants.GitHubAuthBaseUrl}/login/oauth/authorize?client_id={clientIdDTO.ClientId}&scope=repo%20read:user&state={_sessionId}";
        }

        public static async Task AuthorizeSession(Uri callbackUri)
        {
            var code = HttpUtility.ParseQueryString(callbackUri.Query).Get("code");
            var state = HttpUtility.ParseQueryString(callbackUri.Query).Get("state");

            try
            {
                if (string.IsNullOrEmpty(code))
                    throw new Exception("Invalid Authorization Code");

                if (state != _sessionId)
                    throw new InvalidOperationException("Invalid SessionId");

                _sessionId = string.Empty;

                var generateTokenDTO = new GenerateTokenDTO(code, state);
                var token = await AzureFunctionsApiService.GenerateGitTrendsOAuthToken(generateTokenDTO).ConfigureAwait(false);

                await SaveGitHubToken(token).ConfigureAwait(false);

                Alias = await GitHubGraphQLApiService.GetCurrentUserLogin().ConfigureAwait(false);

                OnAuthorizeSessionCompleted(true);
            }
            catch
            {
                OnAuthorizeSessionCompleted(false);
                throw;
            }
        }

        public static async Task<GitHubToken> GetGitHubToken()
        {
            var serializedToken = await SecureStorage.GetAsync(_oauthTokenKey).ConfigureAwait(false);

            try
            {
                return await Task.Run(() => JsonConvert.DeserializeObject<GitHubToken>(serializedToken)).ConfigureAwait(false);
            }
            catch (ArgumentNullException)
            {
                return null;
            }
        }

        static async Task SaveGitHubToken(GitHubToken token)
        {
            if (token is null)
                throw new ArgumentNullException(nameof(token));

            if (token.AccessToken is null)
                throw new ArgumentNullException(nameof(token.AccessToken));

            var serializedToken = await Task.Run(() => JsonConvert.SerializeObject(token)).ConfigureAwait(false);
            await SecureStorage.SetAsync(_oauthTokenKey, serializedToken).ConfigureAwait(false);
        }

        static void OnAuthorizeSessionCompleted(bool isSessionAuthorized) =>
            _authorizeSessionCompletedEventManager.HandleEvent(null, new AuthorizeSessionCompletedEventArgs(isSessionAuthorized), nameof(AuthorizeSessionCompleted));
        #endregion
    }
}