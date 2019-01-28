﻿using System;
using System.Net.Http;
using ClrProfiler.Trace.Internal;
using OpenTracing;
using OpenTracing.Propagation;
using OpenTracing.Tag;

namespace ClrProfiler.Trace.Hooks.Http
{
    public class SystemHttpClient: IWrapper
    {
        private const string TypeName = "System.Net.Http.HttpClient";
        private const string AssemblyName = "System.Net.Http";

        private const string SendAsync = "SendAsync";

        private readonly ITracer _tracer;

        public SystemHttpClient(ITracer tracer)
        {
            _tracer = tracer;
        }

        public EndMethodDelegate BeforeWrappedMethod(TraceMethodInfo traceMethodInfo)
        {
            var request = (HttpRequestMessage)traceMethodInfo.MethodArguments[0];

            var scope = _tracer.BuildSpan("http.out")
                .WithTag(Tags.SpanKind, Tags.SpanKindClient)
                .WithTag(Tags.Component, "httpclient")
                .WithTag(Tags.HttpMethod, request.Method.ToString())
                .WithTag(Tags.HttpUrl, request.RequestUri.ToString())
                .WithTag(Tags.PeerHostname, request.RequestUri.Host)
                .WithTag(Tags.PeerPort, request.RequestUri.Port)
                .StartActive();

            _tracer.Inject(scope.Span.Context, BuiltinFormats.HttpHeaders, new HttpHeadersInjectAdapter(request.Headers));

            traceMethodInfo.TraceContext = scope;

            return delegate(object returnValue, Exception ex)
            {
                TraceDelegateHelper.AsyncTaskResultMethodEnd(Leave, traceMethodInfo, ex, returnValue);
            };
        }

        private void Leave(TraceMethodInfo traceMethodInfo, object ret, Exception ex)
        {
            var scope = (IScope)traceMethodInfo.TraceContext;
            if (ex != null)
            {
                scope.Span.SetException(ex);
            }

            var response = (HttpResponseMessage) ret;
            if (response != null)
            {
                scope.Span.SetTag(Tags.HttpStatus, (int)response.StatusCode);
            }

            scope.Span.Finish();
        }

        public bool CanWrap(TraceMethodInfo traceMethodInfo)
        {
            if (traceMethodInfo.InvocationTarget == null)
            {
                return false;
            }
            var invocationTargetType = traceMethodInfo.InvocationTarget.GetType();
            var assemblyName = invocationTargetType.Assembly.GetName().Name;
            if (assemblyName == AssemblyName && TypeName == traceMethodInfo.TypeName)
            {
                if (traceMethodInfo.MethodName == SendAsync)
                {
                    return true;
                }
            }

            return false;
        }
    }
}