using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using static FBEditor.Core.AppGlobals; // Build, SettingsPath, AppPath

namespace FBEditor.Core;

/// <summary>
/// Built-in AI coding assistant: talks to the Anthropic Messages API over HttpClient.
/// Ported from Modules/AIChatManager.vb. Fully cross-platform.
/// </summary>
public class AIChatManager
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly List<ChatMessage> _chatHistory = new();
    private string _lastResponse = "";

    public bool IsBusy { get; set; }
    public string LastResponse { get => _lastResponse; set => _lastResponse = value; }

    public class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public string LoadAPIKey()
    {
        try
        {
            // First check configured path
            if (!string.IsNullOrEmpty(Build.APIKeyFilePath) && File.Exists(Build.APIKeyFilePath))
                return File.ReadAllText(Build.APIKeyFilePath).Trim();
            // Check settings directory (~/.config/FBEditor on Linux)
            var settingsKeyPath = Path.Combine(SettingsPath, "api_key.txt");
            if (File.Exists(settingsKeyPath))
                return File.ReadAllText(settingsKeyPath).Trim();
            // Fall back to app directory
            var defaultPath = Path.Combine(AppPath, "api_key.txt");
            if (File.Exists(defaultPath))
                return File.ReadAllText(defaultPath).Trim();
            return "";
        }
        catch
        {
            return "";
        }
    }

    public async Task<string> SendMessageAsync(string userMessage, bool includeCode = false,
                                               string code = "", string fileName = "")
    {
        if (IsBusy) return "Please wait for the current response to complete.";

        var apiKey = LoadAPIKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            return "Error: No API key found." + Environment.NewLine +
                   "Configure the API key file path in Build > Build Options," + Environment.NewLine +
                   "or place 'api_key.txt' in: " + SettingsPath;
        }

        // Build full message
        var fullMsg = userMessage;
        if (includeCode && !string.IsNullOrEmpty(code))
        {
            fullMsg += Environment.NewLine + Environment.NewLine +
                       $"Here is my FreeBASIC code ({fileName}):" + Environment.NewLine +
                       "```freebasic" + Environment.NewLine + code + Environment.NewLine + "```";
        }

        // Add to history
        _chatHistory.Add(new ChatMessage { Role = "user", Content = fullMsg });

        // Keep only last 10 exchanges to avoid token limits
        while (_chatHistory.Count > 20)
            _chatHistory.RemoveAt(0);

        IsBusy = true;
        try
        {
            var response = await CallClaudeAPIAsync(apiKey, _chatHistory);
            _lastResponse = response;
            _chatHistory.Add(new ChatMessage { Role = "assistant", Content = response });
            return response;
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<string> CallClaudeAPIAsync(string apiKey, List<ChatMessage> messages)
    {
        var systemPrompt =
            "You are a helpful FreeBASIC programming assistant integrated into the FBEditor IDE. " +
            "Help with FreeBASIC code, debugging, syntax, and programming concepts. " +
            "Keep responses concise and focused. When showing code, use FreeBASIC syntax.\n\n" +
            "CRITICAL FreeBASIC syntax rules you MUST follow:\n" +
            "- Comments use a single apostrophe: ' This is a comment\n" +
            "- Or REM keyword: REM This is a comment\n" +
            "- NEVER use // for comments. FreeBASIC does NOT support // comments.\n" +
            "- NEVER use /* */ block comments. FreeBASIC does NOT support them.\n" +
            "- String literals use double quotes: \"hello\" not 'hello'\n" +
            "- Variable declaration: Dim x As Integer\n" +
            "- Always wrap code in ```freebasic code blocks.\n" +
            "- If you define Sub Main(), you MUST call Main at the end of the file.";

        var msgArray = new JArray();
        foreach (var msg in messages)
            msgArray.Add(new JObject { ["role"] = msg.Role, ["content"] = msg.Content });

        // Updated from the older 4.5-generation string to a current model.
        var requestBody = new JObject
        {
            ["model"] = "claude-sonnet-4-6",
            ["max_tokens"] = 4096,
            ["system"] = systemPrompt,
            ["messages"] = msgArray
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var errObj = JObject.Parse(responseText);
            var errMsg = errObj?.SelectToken("error.message")?.ToString();
            return $"API Error ({response.StatusCode}): {errMsg ?? responseText}";
        }

        // Parse response
        var json = JObject.Parse(responseText);
        var contentArray = json.SelectToken("content");
        if (contentArray != null)
        {
            var sb = new StringBuilder();
            foreach (var item in contentArray)
            {
                if (item.Value<string>("type") == "text")
                    sb.Append(item.Value<string>("text"));
            }
            return sb.ToString();
        }

        return "Could not parse response.";
    }

    public void ClearHistory()
    {
        _chatHistory.Clear();
        _lastResponse = "";
    }

    /// <summary>Extract code from markdown code blocks in AI response.</summary>
    public static string ExtractCodeFromResponse(string response)
    {
        if (string.IsNullOrEmpty(response)) return "";

        // Try various code block markers
        string[] markers =
        {
            "```freebasic\n", "```freebasic\r\n",
            "```basic\n", "```basic\r\n",
            "```fb\n", "```fb\r\n",
            "```\n", "```\r\n"
        };

        foreach (var marker in markers)
        {
            var startIdx = response.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIdx >= 0)
            {
                startIdx += marker.Length;
                var endIdx = response.IndexOf("```", startIdx);
                if (endIdx > startIdx)
                {
                    var code = response.Substring(startIdx, endIdx - startIdx).TrimEnd();
                    return FixFreeBASICCode(code);
                }
            }
        }

        // Check if it looks like pure code
        var trimmed = response.TrimStart().ToUpper();
        if (trimmed.StartsWith("'") || trimmed.StartsWith("DIM ") ||
            trimmed.StartsWith("PRINT") || trimmed.StartsWith("SUB ") ||
            trimmed.StartsWith("FUNCTION ") || trimmed.StartsWith("#INC") ||
            trimmed.StartsWith("DECLARE "))
        {
            return FixFreeBASICCode(response.Trim());
        }

        return "";
    }

    /// <summary>Fix common AI-generated code issues for FreeBASIC.</summary>
    private static string FixFreeBASICCode(string code)
    {
        // Fix smart quotes
        code = code.Replace('\u2018', '\'').Replace('\u2019', '\'');
        code = code.Replace('\u201C', '"').Replace('\u201D', '"');

        var lines = code.Split('\n');
        var result = new StringBuilder();
        bool inBlockComment = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            if (inBlockComment)
            {
                var endBlock = line.IndexOf("*/");
                if (endBlock >= 0)
                {
                    line = "' " + line.Substring(0, endBlock);
                    inBlockComment = false;
                }
                else
                {
                    line = "' " + line;
                }
            }
            else
            {
                // Fix // comments (but not URLs like http://)
                bool inStr = false;
                for (int j = 0; j < line.Length - 1; j++)
                {
                    var c = line[j];
                    if (c == '"')
                    {
                        inStr = !inStr;
                    }
                    else if (!inStr && line.Substring(j, 2) == "//")
                    {
                        if (j == 0 || line[j - 1] != ':')
                        {
                            line = line.Substring(0, j).TrimEnd() + " ' " + line.Substring(j + 2).TrimStart();
                            break;
                        }
                    }
                    else if (!inStr && c == '\'')
                    {
                        break;
                    }
                }

                // Fix /* block comments
                if (!inStr)
                {
                    var blockStart = line.IndexOf("/*");
                    if (blockStart >= 0)
                    {
                        var blockEnd = line.IndexOf("*/", blockStart + 2);
                        if (blockEnd >= 0)
                        {
                            var before = line.Substring(0, blockStart).TrimEnd();
                            var comment = line.Substring(blockStart + 2, blockEnd - blockStart - 2).Trim();
                            var after = line.Substring(blockEnd + 2).TrimStart();
                            line = (before + " " + after).TrimEnd() + " ' " + comment;
                        }
                        else
                        {
                            line = line.Substring(0, blockStart) + " ' " + line.Substring(blockStart + 2);
                            inBlockComment = true;
                        }
                    }
                }
            }

            if (i > 0) result.AppendLine();
            result.Append(line);
        }

        return result.ToString();
    }
}
