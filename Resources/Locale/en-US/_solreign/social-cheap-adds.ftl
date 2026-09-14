# SOLREIGN social cheap-adds copy pack (council memo docs/council/2026-07-16-design-magnetism.md,
# item 5): the SolreignChirp greet emote, the once-per-account social-first milestone toasts, and
# the one-time third-visit wingmate volunteer prompt. House voice: corporate-sinister-but-warm,
# PG-13. CLOSED VOCABULARY: no variables at all — nothing in this file ever renders player,
# attacker, or daemon text.

## The chirp emote (Resources/Prototypes/_Solreign/emotes.yml). The chat message reads as a warm
## GREETING, not birdsong — the emote is the social verb a newcomer can use before learning chat.

chat-emote-name-solreign-chirp = Chirp
chat-emote-msg-solreign-chirp = chirps a friendly greeting!

## The milestone toast frame (Content.Server._Solreign.Notifications.SolreignAwardPopup.
## ShowMilestone). Deliberately carries NO number — social firsts award nothing and deduct nothing;
## a fabricated "+N" would be dishonest (the salary-stipend popup's law). $reason is a loc-resolved
## milestone name from this file — never raw daemon or player text.

solreign-award-milestone-popup = MILESTONE LOGGED — { $reason }

## Milestone names (popup) + private chat mirrors (screenshot-surviving), one pair per flag in
## Content.Server._Solreign.Social.SolreignSocialFirstFlags.

solreign-social-first-chirp-answered-reason = First Reciprocal Chirp
solreign-social-first-chirp-answered-chat = SOCIAL METRICS NOTICE: you chirped, and somebody chirped back. Reciprocal acknowledgment has been entered into your permanent file. The Company finds this development... promising.

solreign-social-first-healed-reason = First Field Repair
solreign-social-first-healed-chat = MEDICAL LEDGER NOTICE: another Asset has restored your operating condition, unbilled. Solreign classifies this behavior as "colleague." It has been noted. Warmly.

solreign-social-first-item-received-reason = First Asset-to-Asset Transfer
solreign-social-first-item-received-chat = INVENTORY NOTICE: an item has changed hands in your favor. No requisition form was filed. None will be required. Generosity between Assets depreciates nothing on our books.

## The one-time third-visit wingmate volunteer prompt (SolreignWingmatePromptSystem) — private
## popup + chat, "yesterday's rescued becomes today's rescuer." The copy promises it will not
## repeat; the social_firsts claim row is what keeps that promise.

solreign-wingmate-prompt-popup = PERSONNEL SUGGESTION: the Wingmate beacon accepts volunteers.
solreign-wingmate-prompt-chat = PERSONNEL SUGGESTION: our records indicate this is not your first shift. Someone once showed you where the air is kept; the Wingmate beacon accepts volunteers who remember what being new felt like. Yesterday's rescued makes today's finest rescuer. This suggestion will not be repeated. The Company simply... noticed you.
