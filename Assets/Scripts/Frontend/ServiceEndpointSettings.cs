using System;
using UnityEngine;

namespace BumperTanks.Frontend
{
    [CreateAssetMenu(fileName = "ServiceEndpointSettings", menuName = "Bumper Tanks/Service Endpoint Settings")]
    public sealed class ServiceEndpointSettings : ScriptableObject
    {
        public const string ResourceName = "ServiceEndpointSettings";

        [SerializeField] private string apiBaseUrl = string.Empty;
        [SerializeField] private string realtimeWebSocketUrl = string.Empty;

        public string ApiBaseUrl => apiBaseUrl;
        public string RealtimeWebSocketUrl => realtimeWebSocketUrl;

        public static ServiceEndpointSettings Load() => Resources.Load<ServiceEndpointSettings>(ResourceName);

        public static bool TryResolve(string configuredApiBaseUrl, string configuredRealtimeUrl, bool developmentBuild,
            out ResolvedServiceEndpoints endpoints, out string error)
        {
            endpoints = default;
            error = null;
            var api = (configuredApiBaseUrl ?? string.Empty).Trim();
            var realtime = (configuredRealtimeUrl ?? string.Empty).Trim();

            if (api.Length == 0)
            {
                if (!developmentBuild)
                {
                    error = "Online services are not configured. Set the API base URL in Resources/ServiceEndpointSettings before building a release.";
                    return false;
                }
                api = "http://127.0.0.1:5188";
            }

            if (!TryParseBaseUrl(api, out var apiUri, out error)) return false;
            if (!developmentBuild && (apiUri.Scheme != Uri.UriSchemeHttps || IsLoopbackHost(apiUri)))
            {
                error = "Release builds require an HTTPS API endpoint on a non-loopback host.";
                return false;
            }

            var resolvedRealtime = realtime;
            if (resolvedRealtime.Length == 0)
            {
                var websocketScheme = apiUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
                var path = apiUri.AbsolutePath.TrimEnd('/');
                var websocketBuilder = new UriBuilder(apiUri)
                {
                    Scheme = websocketScheme,
                    Path = path + "/v1/realtime",
                    Query = string.Empty,
                    Fragment = string.Empty
                };
                if (apiUri.IsDefaultPort) websocketBuilder.Port = -1;
                resolvedRealtime = websocketBuilder.Uri.AbsoluteUri.TrimEnd('/');
            }

            if (!TryParseRealtimeUrl(resolvedRealtime, out var realtimeUri, out error)) return false;
            if (!developmentBuild && (realtimeUri.Scheme != "wss" || IsLoopbackHost(realtimeUri)))
            {
                error = "Release builds require a secure wss:// realtime endpoint on a non-loopback host.";
                return false;
            }

            endpoints = new ResolvedServiceEndpoints(apiUri.AbsoluteUri.TrimEnd('/'), realtimeUri.AbsoluteUri.TrimEnd('/'));
            return true;
        }

        public static bool TryValidateRealtimeOverride(string candidate, bool developmentBuild, out string normalized, out string error)
        {
            normalized = string.Empty;
            if (!TryParseRealtimeUrl((candidate ?? string.Empty).Trim(), out var uri, out error)) return false;
            if (!developmentBuild && (uri.Scheme != "wss" || IsLoopbackHost(uri)))
            {
                error = "Release builds require a secure wss:// realtime endpoint on a non-loopback host.";
                return false;
            }
            normalized = uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        private static bool IsLoopbackHost(Uri uri) => uri.IsLoopback ||
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

        private static bool TryParseBaseUrl(string value, out Uri uri, out string error)
        {
            error = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "The API base URL must be an absolute http:// or https:// URL without credentials, query, or fragment.";
                return false;
            }
            return true;
        }

        private static bool TryParseRealtimeUrl(string value, out Uri uri, out string error)
        {
            error = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss") ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "The realtime URL must be an absolute ws:// or wss:// URL without credentials, query, or fragment.";
                return false;
            }
            return true;
        }
    }

    public readonly struct ResolvedServiceEndpoints
    {
        public ResolvedServiceEndpoints(string apiBaseUrl, string realtimeWebSocketUrl)
        {
            ApiBaseUrl = apiBaseUrl;
            RealtimeWebSocketUrl = realtimeWebSocketUrl;
        }

        public string ApiBaseUrl { get; }
        public string RealtimeWebSocketUrl { get; }
    }
}
