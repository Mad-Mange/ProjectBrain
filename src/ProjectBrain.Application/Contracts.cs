using ProjectBrain.Domain;
namespace ProjectBrain.Application;
// REST and MCP share this application contract.
public interface IKnowledgeService
{
    object Capture(CaptureRequest input, string? key);
    object Propose(ProposalRequest input);
    object Get(long id);
    object Search(SearchRequest input);
    object Edit(long id, EditRequest input, string key);
    object Review(long id, DecisionRequest input, string key);
    object Intent(long id, IntentRequest input, string key);
    object Plan(long id, PlanRequest input, string key);
    object ProposeResult(string id, ResultRequest input, string actor);
    object ReviewResult(string id, ResultReviewRequest input, string key);
}
public interface ISemanticSearchProvider { bool Enabled { get; } }
public sealed class DisabledSemanticSearch : ISemanticSearchProvider { public bool Enabled => false; }

