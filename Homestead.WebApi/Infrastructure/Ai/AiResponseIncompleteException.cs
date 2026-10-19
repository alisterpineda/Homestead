namespace Homestead.WebApi.Infrastructure.Ai;

// A model call that succeeded at the HTTP level but did not produce a complete answer: the model declined the
// request (a refusal), ran out of output tokens, or stopped for any reason other than finishing its turn. Thrown instead of
// handing a blank or cut-off reply to the JSON parser, so the failure reads as what it is. Not transient:
// JobFailureClassifier's allow-list does not retry it.
public class AiResponseIncompleteException(string message) : Exception(message);
