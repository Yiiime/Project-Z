// Transport fixture only. Never presented as an evaluation of model semantics.
export function reviewReply(payload, decision='allow') {
  return {requestId:payload.requestId,decision,reason:'fixture verdict',evidenceIds:[],
    candidateId:payload.context.candidate.id,revision:payload.context.revision,promptVersion:'fixture-review-v1'};
}
