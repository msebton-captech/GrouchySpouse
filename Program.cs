﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using NAudio.Wave;
using System.IO;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.CognitiveServices.Speech;

namespace GrouchySpouse
{
    class Program
    {
        private static readonly HttpClient _openAIClient = new();
        private static string? SYSTEM_PROMPT;
        
        private static string? openApiToken;
        private static string? subscriptionKey;
        private static string? subscriptionRegion;

        static async Task Main(string[] args)
        {
            Console.Clear();
            InitializeClients();
            SYSTEM_PROMPT = await File.ReadAllTextAsync("system_prompt.txt"); // read the system prompt from a flat text file

            //Console.WriteLine("\nSYSTEM PROMPT:\n" + SYSTEM_PROMPT + "\n");
            await Chat();        
        }

        static void InitializeClients()
        {
            var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

            openApiToken = config["OpenApiToken"];
            if (string.IsNullOrEmpty(openApiToken))
            {
                throw new InvalidOperationException("OpenApi Token is not configured in user secrets.");
            }

            subscriptionKey = config["SpeechServiceSubscriptionKey"];
            if (string.IsNullOrEmpty(subscriptionKey))
            {
                throw new InvalidOperationException("Speech Service Subscription Key is not configured in user secrets.");
            }

            subscriptionRegion = config["SpeechServiceRegion"];
            if (string.IsNullOrEmpty(subscriptionRegion))
            {
                throw new InvalidOperationException("Speech Service Subscription Region is not configured in user secrets.");
            }

            // DeepSeek or other "Open" AI API
            //_openAIClient.BaseAddress = new Uri("https://api.deepseek.com/");

            _openAIClient.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
            //_openAIClient.DefaultRequestHeaders.Authorization = 
            //    new AuthenticationHeaderValue("Bearer", "sk-XXX");

            _openAIClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", openApiToken);
        }

        /// <summary>
        /// Chat loop
        /// </summary>
        /// <returns></returns>
        static async Task Chat()
        {
            var history = new List<ChatMessage>();
            if (SYSTEM_PROMPT != null)
            {
                history.Add(new ChatMessage { Role = "system", Content = SYSTEM_PROMPT });
            }

            // This keeps the chat going and feeds back the history to maintain context.
            while (true)
            {
                Console.Write("You: ");
                var userInput = Console.ReadLine();
                if (!string.IsNullOrEmpty(userInput))
                {
                    history.Add(new ChatMessage { Role = "user", Content = userInput });
                }
                else
                {
                    Console.WriteLine("If you want her to talk, you have to give me something to say!");
                }

                //var response = await _openAIClient.PostAsJsonAsync("chat/completions", new
                //{
                //    model = "deepseek-chat",
                //    messages = history,
                //    stream = false
                //});


                var response = await _openAIClient.PostAsJsonAsync("chat/completions", new
                {
                    model = "llama-3.3-70b-versatile",
                    messages = history,
                    stream = false
                });

                var completion = await response.Content.ReadFromJsonAsync<OpenAIResponse>();

                // check for a null completion...
                var answer = completion?.Choices?[0]?.Message?.Content ?? "No response from the model...";
                
                Console.WriteLine(answer);
                history.Add(new ChatMessage { Role = "assistant", Content = answer });
                
                await SynthesizeAndPlayAudio(answer, subscriptionKey, subscriptionRegion);
            }
        }

        /// <summary>
        /// The talking part of the bot
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        static async Task SynthesizeAndPlayAudio(string text, string? subscriptionKey, string? subscriptionRegion)
        {
            var config = SpeechConfig.FromSubscription(subscriptionKey, subscriptionRegion);
            config.SpeechSynthesisVoiceName = "en-US-AriaNeural";

            await CreateAudio(text, config);
        }

        /// <summary>
        /// Obtains audio from the Speech Service
        /// </summary>
        /// <param name="text"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        static async Task CreateAudio(string text, SpeechConfig config)
        {
            using (var synthesizer = new SpeechSynthesizer(config))
            {
                using (var result = await synthesizer.SpeakTextAsync(text))
                {
                    if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                    {
                        Console.WriteLine($"Speech synthesized for text [{text}]");
                    }
                    else if (result.Reason == ResultReason.Canceled)
                    {
                        Console.WriteLine("No audio will be played.");
#if DEBUG
                        var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                        Console.WriteLine($"CANCELED: Reason={cancellation.Reason}");

                        if (cancellation.Reason == CancellationReason.Error)
                        {
                            Console.WriteLine($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                            Console.WriteLine($"CANCELED: ErrorDetails=[{cancellation.ErrorDetails}]");
                            Console.WriteLine($"CANCELED: Did you update the subscription info?");
                        }
#endif
                    }
                }
            }
        }
    }

    public class ChatMessage
    {
        public required string Role { get; set; }
        public required string Content { get; set; }
    }

    public class OpenAIResponse
    {
        public required List<Choice> Choices { get; set; }

        public class Choice
        {
            public required Message Message { get; set; }
        }

        public class Message
        {
            public required string Content { get; set; }
        }
    }
}