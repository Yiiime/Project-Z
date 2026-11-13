import test from "node:test";
import assert from "node:assert/strict";
import { portraitExpression } from "../lib/portrait-expression.mjs";

test("Z can choose to smile without dialogue keywords overriding that choice", () => {
  for (const line of [
    "今天海面挺稳。笑一下？行，给你——不过别让我一直笑，脸会僵的。",
    "今天海面挺稳。笑给你看——不过可不是每回都白笑的，下次得拿点新鲜事来换。",
  ]) {
    for (const metadata of [undefined, "neutral", "happy"])
      assert.equal(portraitExpression("z", metadata, line), metadata === "happy" ? "happy" : "neutral");
  }
});

test("a request, refusal, quote or withdrawn offer does not cue a smile", () => {
  for (const line of [
    "笑一下？", "别让我笑，我不笑。", "她笑给你看。",
    "李庭说：“笑一下？行，给你。”", "笑给你看——算了，我收回。",
  ]) assert.equal(portraitExpression("z", undefined, line), "neutral");
});
