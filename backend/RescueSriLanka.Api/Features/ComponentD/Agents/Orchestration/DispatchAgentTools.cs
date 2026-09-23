// The allow-listed tool set for the Dispatch Recommendation Agent.
// Per the lecture's "least privilege" guidance (Lecture 07, Part 2):
// this agent gets exactly two read-only tools and nothing else — no
// write access, no shell, no arbitrary queries. It cannot create,
// modify, or delete anything; it can only read team data to form a
// recommendation, which the guardrail and human approval gate check
// afterward.
//
// UPDATED: now also exposes GetToolDefinitions(), a provider-neutral
// list (name/description/parameters only) alongside the existing
// Ollama-shaped GetToolSchemas(). Different LLM providers want
// different wire shapes for the same tool metadata — Ollama nests
// under "function", Gemini's Interactions API is flat — so the raw
// definitions live here once, and each provider-specific agent formats
// them however its API expects, without duplicating the tool metadata
// itself.

using System.Text.Json;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Features.ComponentD.Agents.Orchestration
{
    public record ToolDefinition(string Name, string Description, object Parameters);

    public class DispatchAgentTools
    {
        private readonly ITeamMatchingService _matchingService;
        private readonly IRescueTeamService _teamService;

        public DispatchAgentTools(ITeamMatchingService matchingService, IRescueTeamService teamService)
        {
            _matchingService = matchingService;
            _teamService = teamService;
        }

        // Provider-neutral tool metadata — the single source of truth
        // for what this agent is allowed to call.
        public List<ToolDefinition> GetToolDefinitions() => new()
        {
            new ToolDefinition(
                "search_teams",
                "Find rescue teams that currently have an available member with a given skill.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        requiredSkill = new
                        {
                            type = "string",
                            description = "One of: WaterRescue, FirstAid, Paramedic, StructuralCollapse, FireResponse, Logistics, Driving"
                        }
                    },
                    required = new[] { "requiredSkill" }
                }),
            new ToolDefinition(
                "get_team_details",
                "Get full roster and vehicle details for one specific rescue team by its id.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        teamId = new { type = "string", description = "The rescue team's GUID" }
                    },
                    required = new[] { "teamId" }
                })
        };

        // Gemini Interactions API's wire shape (flat — no nested "function").
        // https://ai.google.dev/gemini-api/docs/function-calling
        public object[] GetGeminiToolSchemas() => GetToolDefinitions().Select(t => (object)new
        {
            type = "function",
            name = t.Name,
            description = t.Description,
            parameters = t.Parameters
        }).ToArray();

        // Dispatches a tool call by name to its executor. Returns null if
        // the name isn't one of the allow-listed tools above — the
        // caller treats that as a tool-call failure, not a crash.
        public async Task<string?> ExecuteAsync(string toolName, JsonElement arguments)
        {
            switch (toolName)
            {
                case "search_teams":
                {
                    if (!arguments.TryGetProperty("requiredSkill", out var skillProp)
                        || !Enum.TryParse<SkillType>(skillProp.GetString(), out var skill))
                    {
                        return JsonSerializer.Serialize(new { error = "Invalid or missing requiredSkill." });
                    }

                    var results = await _matchingService.FindMatchesAsync(new MatchRequestDto(skill, null, null, null));
                    return JsonSerializer.Serialize(results);
                }

                case "get_team_details":
                {
                    if (!arguments.TryGetProperty("teamId", out var idProp)
                        || !Guid.TryParse(idProp.GetString(), out var teamId))
                    {
                        return JsonSerializer.Serialize(new { error = "Invalid or missing teamId." });
                    }

                    var team = await _teamService.GetByIdAsync(teamId);
                    return team is null
                        ? JsonSerializer.Serialize(new { error = "Team not found." })
                        : JsonSerializer.Serialize(team);
                }

                default:
                    return null;
            }
        }
    }
}
