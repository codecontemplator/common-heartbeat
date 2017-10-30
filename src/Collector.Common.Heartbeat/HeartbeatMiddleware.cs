﻿using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Context;

namespace Collector.Common.Heartbeat
{
    /// <summary>Represents a middleware that handle heartbeats</summary>
    public class HeartbeatMiddleware<T>
    {
        private readonly Func<T, Task> _healthCheckFunc;
        private readonly RequestDelegate _next;
        private readonly HeartbeatOptions _options;
        private readonly ILogger _logger;

        /// <summary>
        /// Creates a new instance of <see cref="T:Collector.Common.Heartbeat.HeartbeatMiddleware" />
        /// </summary>
        /// <param name="next">The delegate representing the next middleware in the request pipeline.</param>
        /// <param name="logger">The Logger of <see cref="Serilog.ILogger"/>.</param>
        /// <param name="options">The middleware options.</param>
        /// <param name="healthCheckFunc">The <see cref="Func{T, TResult}"/> to excute on <see cref="T"/>.</param>
        public HeartbeatMiddleware(RequestDelegate next, ILogger logger, HeartbeatOptions options, Func<T, Task> healthCheckFunc)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _healthCheckFunc = healthCheckFunc ?? throw new ArgumentNullException(nameof(healthCheckFunc));
        }

        /// <summary>Executes the middleware.</summary>
        /// <param name="httpContext">The <see cref="T:Microsoft.AspNetCore.Http.HttpContext" /> for the current request.</param>
        /// <returns>A task that represents the execution of this middleware.</returns>
        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext == null)
                throw new ArgumentNullException(nameof(httpContext));
            if (IsHttpGet(httpContext))
            {
                await InvokeHeartbeat(httpContext);
            }
            else
            {
                await _next.Invoke(httpContext);
            }
        }

        private static bool IsHttpGet(HttpContext httpContext)
        {
            return httpContext.Request.Method.ToUpperInvariant().Equals("GET");
        }
        private async Task InvokeHeartbeat(HttpContext httpContext)
        {
            var actualKey = httpContext.Request.Headers[_options.ApiKeyHeaderKey];
            if (IsAuthorizedRequest(_options.ApiKey, actualKey))
            {
                var healthCheckMonitor = httpContext.RequestServices.GetService<T>();
                using (LogContext.PushProperty("Heartbeat", true))
                {
                    var statusCode = HttpStatusCode.OK;
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        if (healthCheckMonitor != null)
                        {
                            await _healthCheckFunc.Invoke(healthCheckMonitor);
                        }
                        _logger.Information("Heartbeat API call returned success. Test took {ExecutionTime} ms.", watch.ElapsedMilliseconds);
                    }
                    catch (Exception error)
                    {
                        _logger.Warning("Heartbeat API call returned failure. Exception message was {ExceptionMessage}", error.Message);
                        statusCode = HttpStatusCode.InternalServerError;
                    }
                    watch.Stop();
                    httpContext.Response.StatusCode = (int)statusCode;
                }
            }
            else
            {
                httpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            }
        }

        private static bool IsAuthorizedRequest(string apiKey, string actualKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return true;
            }
            return apiKey.Equals(actualKey);
        }

    }
}