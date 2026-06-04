using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Services.AI;

/// <summary>
/// Configuration options for the AI suggestion service.
/// </summary>
public class AIOptions
{
    public const string SectionName = "AISuggestions";

    /// <summary>Whether the AI suggestion feature is enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The AI provider endpoint URL.
    /// Supports any OpenAI-compatible API (e.g., OpenAI, Azure OpenAI, local LLM).
    /// </summary>
    [Url]
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";

    /// <summary>API key for the AI provider.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The model to use (e.g., gpt-4o-mini, gpt-4o, claude-sonnet-4-20250514).</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Maximum tokens for the AI response.
    /// </summary>
    [Range(50, 4000)]
    public int MaxTokens { get; set; } = 500;

    /// <summary>
    /// Temperature for response creativity (0.0 = deterministic, 1.0 = creative).
    /// </summary>
    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Only generate suggestions if no suggestion was made in the last N minutes
    /// for the same device. Default: 60 minutes.
    /// </summary>
    [Range(1, 1440)]
    public int CooldownMinutes { get; set; } = 60;

    /// <summary>
    /// Optional system prompt override. If empty, a default aeroponic expert prompt is used.
    /// </summary>
    public string? SystemPromptOverride { get; set; }

    /// <summary>
    /// Optional proxy URL for the AI API calls.
    /// </summary>
    public string? ProxyUrl { get; set; }
}
