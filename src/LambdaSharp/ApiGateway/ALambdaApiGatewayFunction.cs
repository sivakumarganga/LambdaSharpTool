﻿/*
 * LambdaSharp (λ#)
 * Copyright (C) 2018-2020
 * lambdasharp.net
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using LambdaSharp.ApiGateway.Internal;
using LambdaSharp.Exceptions;
using LambdaSharp.Logger;

namespace LambdaSharp.ApiGateway {

    /// <summary>
    /// <see cref="ALambdaApiGatewayFunction"/> is the abstract base class for API Gateway functions (both V1 and V2).
    /// </summary>
    /// <remarks>
    /// If the Lambda function was configured for REST API or WebSocket routes with method invocations, then the
    /// Lambda function will act as a controller and route the incoming requests to the specified methods. Alternatively,
    /// a derived class can override <see cref="ALambdaApiGatewayFunction.ProcessProxyRequestAsync(APIGatewayProxyRequest)"/>
    /// to process requests.
    /// </remarks>
    public abstract class ALambdaApiGatewayFunction : ALambdaFunction<APIGatewayProxyRequest, APIGatewayProxyResponse> {

        //--- Types ---
        private class ApiGatewayInvocationMappings {

            //--- Properties ---
            public List<ApiGatewayInvocationMapping>? Mappings { get; set; }
        }

        private class ApiGatewayInvocationMapping {

            //--- Properties ---
            public string? RestApi { get; set; }
            public string? WebSocket { get; set; }
            public string? Method { get; set; }
        }

        //--- Fields ---
        private ApiGatewayInvocationTargetDirectory? _directory;
        private APIGatewayProxyRequest? _currentRequest;

        //--- Constructors ---

        /// <summary>
        /// Initializes a new <see cref="ALambdaApiGatewayFunction"/> instance using the default implementation of <see cref="ILambdaFunctionDependencyProvider"/>.
        /// </summary>
        protected ALambdaApiGatewayFunction() : this(null) { }

        /// <summary>
        /// Initializes a new <see cref="ALambdaApiGatewayFunction"/> instance using a custom implementation of <see cref="ILambdaFunctionDependencyProvider"/>.
        /// </summary>
        /// <param name="provider">Custom implementation of <see cref="ILambdaFunctionDependencyProvider"/>.</param>
        protected ALambdaApiGatewayFunction(ILambdaFunctionDependencyProvider? provider) : base(provider) { }

        //--- Properties ---

        /// <summary>
        /// Retrieve the current <see cref="APIGatewayProxyRequest"/> for the request.
        /// </summary>
        /// <remarks>
        /// This property is only set during the invocation of <see cref="ProcessMessageAsync(APIGatewayProxyRequest)"/>. Otherwise, it throws an <see cref="InvalidOperationException" />.
        /// </remarks>
        /// <value>The <see cref="APIGatewayProxyRequest"/> instance.</value>
        protected APIGatewayProxyRequest CurrentRequest => _currentRequest ?? throw new InvalidOperationException();

        //--- Methods ---

        /// <summary>
        /// The <see cref="InitializeEpilogueAsync()"/> method is invoked to complet the initialization of the
        /// Lambda function. This is the last of three methods that are invoked to initialize the Lambda function.
        /// </summary>
        /// <returns>The task object representing the asynchronous operation.</returns>
        protected override async Task InitializeEpilogueAsync() {
            await base.InitializeEpilogueAsync();

            // NOTE (2019-04-15, bjorg): we initialize the invocation target directory after the function environment
            //  initialization so that 'CreateInvocationTargetInstance()' can access the environment variables if need be.

            // read optional api-gateway-mappings file
            _directory = new ApiGatewayInvocationTargetDirectory(CreateInvocationTargetInstance, LambdaSerializer);
            if(File.Exists("api-mappings.json")) {
                var mappings = LambdaSerializer.Deserialize<ApiGatewayInvocationMappings>(File.ReadAllText("api-mappings.json"));
                if(mappings.Mappings == null) {
                    throw new InvalidDataException("missing 'Mappings' property in 'api-mappings.json' file");
                }
                foreach(var mapping in mappings.Mappings) {
                    if(mapping.Method == null) {
                        throw new InvalidDataException("missing 'Method' property in 'api-mappings.json' file");
                    }
                    if(mapping.RestApi != null) {
                        LogInfo($"Mapping REST API '{mapping.RestApi}' to {mapping.Method}");
                        _directory.Add(mapping.RestApi, mapping.Method);
                    } else if(mapping.WebSocket != null) {
                        LogInfo($"Mapping WebSocket route '{mapping.WebSocket}' to {mapping.Method}");
                        _directory.Add(mapping.WebSocket, mapping.Method);
                    } else {
                        throw new InvalidDataException("missing 'RestApi/WebSocket' property in 'api-mappings.json' file");
                    }
                }
            }
        }

        /// <summary>
        /// The <see cref="ProcessMessageAsync(APIGatewayProxyRequest)"/> method is overridden to
        /// provide specific behavior for this base class.
        /// </summary>
        /// <param name="request">The <see cref="APIGatewayProxyRequest"/> instance.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public override sealed async Task<APIGatewayProxyResponse> ProcessMessageAsync(APIGatewayProxyRequest request) {
            if(_directory == null) {
                throw new ShouldNeverHappenException();
            }

            // NOTE (2020-02-19, bjorg): we rewrite the headers' dictionary to make it case-insensitive
            request.Headers = (request.Headers != null)
                ? new Dictionary<string, string>(request.Headers, StringComparer.InvariantCultureIgnoreCase)
                : new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

            // handle request
            _currentRequest = request;
            APIGatewayProxyResponse response;
            var signature = "<null>";
            try {
                ApiGatewayInvocationTargetDirectory.InvocationTargetDelegate? invocationTarget;
                var requestContext = request.RequestContext;

                // check if this invocation is a REST API request
                if(requestContext.ResourcePath != null) {

                    // this is a REST API request
                    signature = $"{requestContext.HttpMethod}:{requestContext.ResourcePath}";
                    if(!_directory.TryGetInvocationTarget(signature, out invocationTarget)) {

                        // check if a generic HTTP method entry exists
                        _directory.TryGetInvocationTarget($"ANY:{requestContext.ResourcePath}", out invocationTarget);
                    }
                } else if(requestContext.RouteKey != null) {

                    // this is a WebSocket request
                    signature = requestContext.RouteKey;
                    _directory.TryGetInvocationTarget(signature, out invocationTarget);
                } else {

                    // could not determine the request type
                    signature = "<undetermined>";
                    invocationTarget = null;
                }

                // invoke invocation target or derived handler
                LogInfo($"dispatching route '{signature}'");
                if(invocationTarget != null) {
                    response = await invocationTarget(request);
                } else {
                    response = await ProcessProxyRequestAsync(request);
                }
                LogInfo($"finished with status code {response.StatusCode}");
            } catch(ApiGatewayAsyncEndpointException e) {

                // exception was raised by an asynchronous endpoint; the failure needs to be recorded for playback
                LogError(e, $"async route '{signature}' threw {e.GetType()}");
                await RecordFailedMessageAsync(LambdaLogLevel.ERROR, FailedMessageOrigin.ApiGateway, LambdaSerializer.Serialize(request), e);
                return CreateInvocationExceptionResponse(request, e.InnerException ?? e);
            } catch(ApiGatewayInvocationTargetParameterException e) {
                LogInfo($"invalid target invocation parameter '{e.ParameterName}': {e.Message}");
                response = CreateBadParameterResponse(request, e.ParameterName, e.Message);
            } catch(ApiGatewayAbortException e) {
                LogInfo($"aborted with status code {e.Response.StatusCode}\n{e.Response.Body}");
                response = e.Response;
            } catch(Exception e) {
                LogError(e, $"route '{signature}' threw {e.GetType()}");
                response = CreateInvocationExceptionResponse(request, e);
            } finally {
                _currentRequest = null;
            }
            return response;
        }

        /// <summary>
        /// The <see cref="ProcessProxyRequestAsync(APIGatewayProxyRequest)"/> method is invoked when the
        /// received request is not mapped to a method invocation.
        /// </summary>
        /// <remarks>
        /// The default implementation response with a 404 error.
        /// </remarks>
        /// <param name="request">The <see cref="APIGatewayProxyRequest"/> instance.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public virtual Task<APIGatewayProxyResponse> ProcessProxyRequestAsync(APIGatewayProxyRequest request) {

            // NOTE: the default implementation returns NotFound since the derived method is only called when no signature match was found
            var signature = request.RequestContext.RouteKey ?? $"{request.RequestContext.HttpMethod}:{request.RequestContext.ResourcePath}";
            LogInfo($"route '{signature}' not found");
            return Task.FromResult(CreateRouteNotFoundResponse(request, signature));
        }

        /// <summary>
        /// The <see cref="CreateInvocationTargetInstance(Type)"/> method is invoked to create instances for
        /// mapped method invocations. This is invoked only once per requested type.
        /// </summary>
        /// <remarks>
        /// If the requested type is the type of the Lambda function, this method returns the Lambda function instance.
        /// Otherwise, it attempts to instantiate the requested type using that Lambda function instance as the sole the constructor parmeter.
        /// This pattern allows the Lambda function to be the <i>Dependency Provider</i> for the created instance.
        /// </remarks>
        /// <param name="type">The requested type to instantiate.</param>
        /// <returns>The instance for the target method.</returns>
        public virtual object CreateInvocationTargetInstance(Type type) {
            return (type == GetType())
                ? this
                : (Activator.CreateInstance(type, new[] { this }) ?? throw new ShouldNeverHappenException());
        }

        /// <summary>
        /// The <see cref="CreateRouteNotFoundResponse(APIGatewayProxyRequest, string)"/> method creates the
        /// <see cref="APIGatewayProxyResponse"/> instance to report a <c>404 - Not Found</c> error for the requested
        /// REST API or WebSocket route.
        /// </summary>
        /// <param name="request">The <see cref="APIGatewayProxyRequest"/> instance.</param>
        /// <param name="route">The REST API or WebSocket route that could not be found.</param>
        /// <returns>The <see cref="APIGatewayProxyResponse"/> instance.</returns>
        protected virtual APIGatewayProxyResponse CreateRouteNotFoundResponse(APIGatewayProxyRequest request, string route)
            => CreateResponse(404, $"Route '{route}' not found");

        /// <summary>
        /// The <see cref="CreateBadParameterResponse(APIGatewayProxyRequest, string, string)"/> method creates a
        /// <see cref="APIGatewayProxyResponse"/> instance to report a <c>400 - Bad Request</c> error, because
        /// a request parameter was invalid or missing.
        /// </summary>
        /// <param name="request">The <see cref="APIGatewayProxyRequest"/> instance.</param>
        /// <param name="parameterName">The name of the parameter.</param>
        /// <param name="message">The error message.</param>
        /// <returns>The <see cref="APIGatewayProxyResponse"/> instance.</returns>
        protected virtual APIGatewayProxyResponse CreateBadParameterResponse(APIGatewayProxyRequest request, string parameterName, string message)
            => (parameterName == "request")
                ? CreateResponse(400, $"Bad request body: {message}")
                : CreateResponse(400, $"Bad request parameter '{parameterName}': {message}");

        /// <summary>
        /// The <see cref="CreateInvocationExceptionResponse(APIGatewayProxyRequest, Exception)"/> method creates a
        /// <see cref="APIGatewayProxyResponse"/> instance to report a <c>500 - Internal Server Error</c> error, because
        /// an unhandled exception occurred during the request processing.
        /// </summary>
        /// <param name="request">The <see cref="APIGatewayProxyRequest"/> instance.</param>
        /// <param name="exception">The raised exception.</param>
        /// <returns>The <see cref="APIGatewayProxyResponse"/> instance.</returns>
        protected virtual APIGatewayProxyResponse CreateInvocationExceptionResponse(APIGatewayProxyRequest request, Exception exception)
            => CreateResponse(500, "Internal Error (see logs for details)");

        /// <summary>
        /// The <see cref="Abort(APIGatewayProxyResponse)"/> method stops the request processing and sets the
        /// specified <see cref="APIGatewayProxyResponse"/> instance as the response.
        /// </summary>
        /// <remarks>
        /// This method never returns as the abort exception is thrown immediately. The <see cref="Exception"/> instance is shown as returned
        /// to make it easier to tell the compiler the control flow.
        /// </remarks>
        /// <param name="response">The <see cref="APIGatewayProxyResponse"/> instance to use for the response.</param>
        /// <returns>Nothing. See remarks.</returns>
        /// <example>
        /// <code>
        /// throw Abort(new APIGatewayProxyResponse {
        ///     StatusCode = 404,
        ///     Body = "item not found"
        /// });
        /// </code>
        /// </example>
        protected virtual Exception Abort(APIGatewayProxyResponse response)
            => throw new ApiGatewayAbortException(response);

        /// <summary>
        /// The <see cref="AbortBadRequest(string)"/> stops the request processing with a <c>400 - Bad Request</c> response.
        /// </summary>
        /// <remarks>
        /// This method never returns as the abort exception is thrown immediately. The <see cref="Exception"/> instance is shown as returned
        /// to make it easier to tell the compiler the control flow.
        /// </remarks>
        /// <param name="message">The response message.</param>
        /// <returns>Nothing. See remarks.</returns>
        /// <example>
        /// <code>
        /// throw AbortBadRequest("invalid selection");
        /// </code>
        /// </example>
        protected virtual Exception AbortBadRequest(string message)
            => Abort(CreateResponse(400, message));

        /// <summary>
        /// The <see cref="AbortForbidden(string)"/> stops the request processing with a <c>403 - Forbidden</c> response.
        /// </summary>
        /// <remarks>
        /// This method never returns as the abort exception is thrown immediately. The <see cref="Exception"/> instance is shown as returned
        /// to make it easier to tell the compiler the control flow.
        /// </remarks>
        /// <param name="message">The response message.</param>
        /// <returns>Nothing. See remarks.</returns>
        /// <example>
        /// <code>
        /// throw AbortForbidden("you are not authorized");
        /// </code>
        /// </example>
        protected virtual Exception AbortForbidden(string message)
            => Abort(CreateResponse(403, message));

        /// <summary>
        /// The <see cref="AbortNotFound(string)"/> stops the request processing with a <c>404 - Not Found</c> response.
        /// </summary>
        /// <remarks>
        /// This method never returns as the abort exception is thrown immediately. The <see cref="Exception"/> instance is shown as returned
        /// to make it easier to tell the compiler the control flow.
        /// </remarks>
        /// <param name="message">The response message.</param>
        /// <returns>Nothing. See remarks.</returns>
        /// <example>
        /// <code>
        /// throw AbortNotFound("item not found");
        /// </code>
        /// </example>
        protected virtual Exception AbortNotFound(string message)
            => Abort(CreateResponse(404, message));

        /// <summary>
        /// The <see cref="CreateResponse(int, string)"/> method creates a <see cref="APIGatewayProxyResponse"/> instance
        /// set to the specified status code and message in the response body.
        /// </summary>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        /// <param name="message">The response mesage.</param>
        /// <returns>The <see cref="APIGatewayProxyResponse"/> instance.</returns>
        /// <example>
        /// For a REST API route, the response body looks as follows.
        /// <code>
        /// {
        ///     "message": "item not found",
        ///     "requestId": "123abc"
        /// }
        /// </code>
        /// For a WebSocket route, the response body contains in addition the connection ID.
        /// <code>
        /// {
        ///     "message": "item not found",
        ///     "requestId": "123abc",
        ///     "connectionId": "456def"
        /// }
        /// </code>
        /// </example>
        protected virtual APIGatewayProxyResponse CreateResponse(int statusCode, string message) {
            var response = (_currentRequest?.RequestContext.ConnectionId != null)
                ? (object)new {
                    message = message,
                    connectionId = _currentRequest.RequestContext.ConnectionId,
                    requestId = _currentRequest.RequestContext.RequestId
                }
                : new {
                    message = message
                };
            return new APIGatewayProxyResponse {
                StatusCode = statusCode,
                Body = LambdaSerializer.Serialize<object>(response),
                Headers = new Dictionary<string, string> {
                    ["ContentType"] = "application/json"
                }
            };
        }
    }
}
