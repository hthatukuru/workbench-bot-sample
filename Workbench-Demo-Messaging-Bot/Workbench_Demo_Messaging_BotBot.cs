// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.ServiceBus;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Workbench_Demo_Messaging_Bot
{
    /// <summary>
    /// Represents a bot that processes incoming activities.
    /// For each user interaction, an instance of this class is created and the OnTurnAsync method is called.
    /// This is a Transient lifetime service.  Transient lifetime services are created
    /// each time they're requested. For each Activity received, a new instance of this
    /// class is created. Objects that are expensive to construct, or have a lifetime
    /// beyond the single turn, should be carefully managed.
    /// For example, the <see cref="MemoryStorage"/> object and associated
    /// <see cref="IStatePropertyAccessor{T}"/> object are created with a singleton lifetime.
    /// </summary>
    /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.1"/>
    public class Workbench_Demo_Messaging_BotBot : IBot
    {
        private readonly Workbench_Demo_Messaging_BotAccessors _accessors;
        private readonly ILogger _logger;

        const string ServiceBusConnectionString = "<YOUR-SERVICE-BUS-CONNECTION-STRING>";
        
        const string TopicName = "egresstopic";
        const string SubscriptionName = "<YOUR-SUBSCRIPTION-NAME>";
        static ISubscriptionClient subscriptionClient;

        public static string fromId;
        public static string fromName;
        public static string toId;
        public static string toName;
        public static string serviceUrl = "<YOUR-BOT-SERVICE-URL>";
        public static string channelId;
        public static string conversationId;
        public ConnectorClient connector;
        public ChannelAccount userAccount;
        public ChannelAccount botAccount;

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        /// <param name="accessors">A class containing <see cref="IStatePropertyAccessor{T}"/> used to manage state.</param>
        /// <param name="loggerFactory">A <see cref="ILoggerFactory"/> that is hooked to the Azure App Service provider.</param>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.1#windows-eventlog-provider"/>
        public Workbench_Demo_Messaging_BotBot(Workbench_Demo_Messaging_BotAccessors accessors, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new System.ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<Workbench_Demo_Messaging_BotBot>();
            _logger.LogTrace("Turn start.");
            _accessors = accessors ?? throw new System.ArgumentNullException(nameof(accessors));

            subscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, SubscriptionName);
        }

        /// <summary>
        /// Every conversation turn for our Echo Bot will call this method.
        /// There are no dialogs used, since it's "single turn" processing, meaning a single
        /// request and response.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        /// <seealso cref="BotStateSet"/>
        /// <seealso cref="ConversationState"/>
        /// <seealso cref="IMiddleware"/>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Handle Message activity type, which is the main activity type for shown within a conversational interface
            // Message activities may contain text, speech, interactive cards, and binary or unknown attachments.
            // see https://aka.ms/about-bot-activity-message to learn more about the message and other activity types
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                // Get the conversation state from the turn context.
                var state = await _accessors.CounterState.GetAsync(turnContext, () => new CounterState());

                // Bump the turn count for this conversation.
                state.TurnCount++;

                // Set the property using the accessor.
                await _accessors.CounterState.SetAsync(turnContext, state);

                // Save the new turn count into the conversation state.
                await _accessors.ConversationState.SaveChangesAsync(turnContext);

                // Echo back to the user whatever they typed.
                //var responseMessage = $"Turn {state.TurnCount}: You sent '{turnContext.Activity.Text}'\n";
                //await turnContext.SendActivityAsync(responseMessage);
                conversationId = turnContext.Activity.Conversation.Id;
                channelId = turnContext.Activity.ChannelId;
                serviceUrl = turnContext.Activity.ServiceUrl;

                RegisterOnMessageHandlerAndReceiveMessages();

                await Task.Delay(1);
            }
            else
            {
                await turnContext.SendActivityAsync($"{turnContext.Activity.Type} event detected");
            }
        }

        public void RegisterOnMessageHandlerAndReceiveMessages()
        {
            // Configure the message handler options in terms of exception handling, number of concurrent messages to deliver, etc.
            var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
            {
                // Maximum number of concurrent calls to the callback ProcessMessagesAsync(), set to 1 for simplicity.
                // Set it according to how many messages the application wants to process in parallel.
                MaxConcurrentCalls = 1,

                // Indicates whether the message pump should automatically complete the messages after returning from user callback.
                // False below indicates the complete operation is handled by the user callback as in ProcessMessagesAsync().
                AutoComplete = false
            };

            // Register the function that processes messages.
            subscriptionClient.RegisterMessageHandler(ProcessMessagesAsync, messageHandlerOptions);
        }

        public async Task ProcessMessagesAsync(Message message, CancellationToken token)
        {
            // Process the message.
            var messageString = Encoding.UTF8.GetString(message.Body);
            var json = JObject.Parse(messageString);
            var messageName = json["MessageName"].Value<string>();

            if (String.Equals(messageName, "ContractMessage", StringComparison.OrdinalIgnoreCase))
            {
                string titleText;
                var isNewContract = json["IsNewContract"].Value<Boolean>();
                if (isNewContract)
                {
                    titleText = "New Contract Created";
                }
                else
                {
                    titleText = "Contract Updated";
                }
                connector = new ConnectorClient(new Uri(serviceUrl), "<YOUR-MICROSOFT-APP-ID>", "<YOUR-MICROSOFT-APP-PASSWORD>");
                IMessageActivity botMessage = Activity.CreateMessageActivity();
                botMessage.ChannelId = channelId;
                botMessage.Conversation = new ConversationAccount(id: conversationId);
                botMessage.Locale = "en-Us";

                ThumbnailCard card = new ThumbnailCard()
                {
                    Title = titleText,
                    Text = String.Concat("<b>Contract Id : </b>", json["ContractId"].Value<int>().ToString(), "<br>", "<b>Contract Ledger Identifier : </b>", json["ContractLedgerIdentifier"].Value<string>(), "<br>", "<b>Block Id : </b>", json["BlockId"].Value<int>().ToString(), "<br>", "<b>Block Hash : </b>", json["BlockHash"].Value<string>(), "<br><br>"),
                };
                botMessage.Attachments.Add(card.ToAttachment());
                await connector.Conversations.SendToConversationAsync(conversationId, (Activity)botMessage);
            }
            else if (String.Equals(messageName, "EventMessage", StringComparison.OrdinalIgnoreCase))
            {
                var eventName = json["EventName"].Value<string>();
                if (String.Equals(eventName, "ApplicationIngestion", StringComparison.OrdinalIgnoreCase))
                {
                    connector = new ConnectorClient(new Uri(serviceUrl), "<YOUR-MICROSOFT-APP-ID>", "<YOUR-MICROSOFT-APP-PASSWORD>");
                    IMessageActivity botMessage = Activity.CreateMessageActivity();
                    botMessage.ChannelId = channelId;
                    botMessage.Conversation = new ConversationAccount(id: conversationId);
                    botMessage.Locale = "en-Us";
                    var url = json["ApplicationDefinitionLocation"].Value<string>();

                    StringBuilder resourceStringBuilder = new StringBuilder();
                    string resourceSource = "View Application";
                    resourceStringBuilder.AppendFormat("<a href=\"{0}\">{1}</a>", url, resourceSource);

                    ThumbnailCard card = new ThumbnailCard()
                    {
                        Title = string.Format("New Application Uploaded"),
                        Text = String.Concat("<b>Application Name : </b>", json["ApplicationName"].Value<string>(), "<br>", "<b>Application Id : </b>", json["ApplicationId"].Value<int>().ToString(), "<br>", "<b>Application Version : </b>", json["ApplicationVersion"].Value<string>(), "<br>", "<b>Application Definition Location : </b>", resourceStringBuilder.ToString(), "<br><br>"),
                    };
                    botMessage.Attachments.Add(card.ToAttachment());

                    await connector.Conversations.SendToConversationAsync(conversationId, (Activity)botMessage);
                }
                else if (String.Equals(eventName, "RoleAssignment", StringComparison.OrdinalIgnoreCase))
                {
                    var applicationRole = json["ApplicationRole"].Value<ApplicationRole>();
                    connector = new ConnectorClient(new Uri(serviceUrl), "<YOUR-MICROSOFT-APP-ID>", "<YOUR-MICROSOFT-APP-PASSWORD>");
                    IMessageActivity botMessage = Activity.CreateMessageActivity();
                    botMessage.ChannelId = channelId;
                    botMessage.Conversation = new ConversationAccount(id: conversationId);
                    botMessage.Locale = "en-Us";

                    ThumbnailCard card = new ThumbnailCard()
                    {
                        Title = string.Format("New Role Assigned"),
                        Text = String.Concat("<b>Application Name : </b>", json["ApplicationName"].Value<string>(), "<br>", "<b>Application Id : </b>", json["ApplicationId"].Value<int>().ToString(), "<br>", "<b>Application Version : </b>", json["ApplicationVersion"].Value<string>(), "<br>", "<b>Application Role Name : </b>", applicationRole.Name, "<br><br>"),
                    };
                    botMessage.Attachments.Add(card.ToAttachment());

                    await connector.Conversations.SendToConversationAsync(conversationId, (Activity)botMessage);
                }
                else if (String.Equals(eventName, "ContractFunctionInvocation", StringComparison.OrdinalIgnoreCase))
                {
                    connector = new ConnectorClient(new Uri(serviceUrl), "<YOUR-MICROSOFT-APP-ID>", "<YOUR-MICROSOFT-APP-PASSWORD>");
                    IMessageActivity botMessage = Activity.CreateMessageActivity();
                    botMessage.ChannelId = channelId;
                    botMessage.Conversation = new ConversationAccount(id: conversationId);
                    botMessage.Locale = "en-Us";
                    var fnName = json["FunctionName"].Value<string>();
                    if (String.IsNullOrWhiteSpace(fnName))
                    {
                        fnName = "constructor";
                    }
                    ThumbnailCard card = new ThumbnailCard()
                    {
                        Title = string.Format("Contract Function Invocated"),
                        Text = String.Concat("<b>Contract Id : </b>", json["ContractId"].Value<int>().ToString(), "<br>", "<b>Contract Ledger Identifier : </b>", json["ContractLedgerIdentifier"].Value<string>(), "<br>", "<b>Function : </b>", fnName, "<br><br>"),
                    };
                    botMessage.Attachments.Add(card.ToAttachment());

                    await connector.Conversations.SendToConversationAsync(conversationId, (Activity)botMessage);
                }
            }

            // Complete the message so that it is not received again.
            // This can be done only if the subscriptionClient is created in ReceiveMode.PeekLock mode (which is the default).
            await subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);

            // Note: Use the cancellationToken passed as necessary to determine if the subscriptionClient has already been closed.
            // If subscriptionClient has already been closed, you can choose to not call CompleteAsync() or AbandonAsync() etc.
            // to avoid unnecessary exceptions.
        }

        public Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            return Task.CompletedTask;
        }
    }
}
