﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using NAudio.Wave;
using System.IO;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;


namespace GrouchySpouse
{
    class Program
    {
        private static readonly HttpClient _openAIClient = new();
        private static readonly HttpClient _replicateClient = new();

        private static string? SYSTEM_PROMPT;


        static async Task Main(string[] args)
        {
            Console.Clear();
            InitializeClients();
            SYSTEM_PROMPT = await File.ReadAllTextAsync("system_prompt.txt"); // read the system prompt from a flat text file

            Console.WriteLine("\nSYSTEM PROMPT:\n" + SYSTEM_PROMPT + "\n");
            await Chat();        
        }

        static void InitializeClients()
        {
            // DeepSeek or other "Open" AI API
            //_openAIClient.BaseAddress = new Uri("https://api.deepseek.com/");

            _openAIClient.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
            //_openAIClient.DefaultRequestHeaders.Authorization = 
            //    new AuthenticationHeaderValue("Bearer", "sk-XXX");

            _openAIClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", "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");
            
            // Replicate API
            _replicateClient.DefaultRequestHeaders.Authorization = 
               new AuthenticationHeaderValue("Bearer", "r8_2jT3UvKN3Y83jeGEgI1OuucAVH2aa8u23hYQg");
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
                
                await SynthesizeAndPlayAudio(answer);
            }
        }

        /// <summary>
        /// The talking part of the bot
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        static async Task SynthesizeAndPlayAudio(string text)
        {
            var prediction = await CreatePrediction(text);
            var outputUrl = await WaitForPredictionCompletion(prediction.Id);

            if (outputUrl != null)
            {
                var tempFile = Path.GetTempFileName();


#if DEBUG
                Console.WriteLine($"\nWriting \"{outputUrl}\" to \"{tempFile}\"");  // Debug-only logging
#endif
                await DownloadAudioFile(outputUrl, tempFile);
                await PlayAudioAsync(tempFile);
#if DEBUG
                Console.WriteLine("Deleting file \"{0}\"\n", tempFile);
#endif
                File.Delete(tempFile);  // Clean up the temp file after playing
            }
        }

    /// <summary>
    /// Obtains a prediction from the Replicate API
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
        static async Task<ReplicateCreateResponse> CreatePrediction(string text)
        {
            var response = await _replicateClient.PostAsJsonAsync("https://api.replicate.com/v1/predictions", new
            {
                version = "dfdf537ba482b029e0a761699e6f55e9162cfd159270bfe0e44857caa5f275a6",
                input = new
                {
                    text = text,
                    language = "en",
                    temperature = 0.5,
                    length = 1.0,
                    speed = 1.1,
                    voice = "af_bella" // af_bella, af_zoe, af_lisa, af_mia, af_samantha, af_olivia, af_isabella
                }
            });

            var result = await response.Content.ReadFromJsonAsync<ReplicateCreateResponse>() ?? throw new InvalidOperationException("Failed to deserialize the response.");
            return result;
        }

        /// <summary>
        /// Waits for the prediction to complete
        /// </summary>
        /// <param name="predictionId"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        static async Task<string> WaitForPredictionCompletion(string predictionId)
        {
            ReplicateStatusResponse statusResponse;
            do
            {
                await Task.Delay(1000);
                Console.WriteLine("Waiting for Replicate API response...");
                var response = await _replicateClient.GetAsync(
                    $"https://api.replicate.com/v1/predictions/{predictionId}");
                
                statusResponse = await response.Content.ReadFromJsonAsync<ReplicateStatusResponse>() ?? throw new InvalidOperationException("Failed to deserialize the response.");
            } while (statusResponse.Status == "starting" || statusResponse.Status == "processing");

            return statusResponse.Status == "succeeded" && statusResponse.Output != null ? statusResponse.Output : string.Empty;
        }

        /// <summary>
        /// Downloads the audio stream to a file
        /// </summary>
        /// <param name="url"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        static async Task DownloadAudioFile(string url, string filePath)
        {
            using var httpClient = new HttpClient();
            var audioBytes = await httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(filePath, audioBytes);
        }

        /// <summary>
        /// Give her a voice. Multi-platform audio playback.
        /// </summary>
        /// <param name="filePath"></param>
        private static async Task PlayAudioAsync(string filePath)
        {
            // Different OS's handle audio differently. 
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // MacOS
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "afplay";
                process.StartInfo.Arguments = filePath;
                process.Start();
                await process.WaitForExitAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows
                var player = new System.Media.SoundPlayer(filePath);
                player.PlaySync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "aplay";
                process.StartInfo.Arguments = filePath;
                process.Start();
                await process.WaitForExitAsync();
            }
            else
            {
                throw new NotSupportedException("Your OS is not supported for audio playback.");
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

    public class ReplicateCreateResponse
    {
        public required string Id { get; set; }
    }

    public class ReplicateStatusResponse
    {
        public required string Status { get; set; }
        public required string Output { get; set; }
    }
}