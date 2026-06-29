# Product

## Register

product

## Users

Cantaro is for people who have spent years collecting music and media across platforms and want that personal archive to stay useful, portable, and theirs. The primary user is a self-hosting or technically comfortable listener/viewer who wants a calm way to connect services, preserve long-lived playlists, review sync decisions, and keep anime or media lists current without turning the product into another platform clone.

The web app is the primary product surface. `@cantaro/client-shared` is part of that surface as the shared component and page library used by the web app and browser extension, so shared UI should follow the same product intent.

## Product Purpose

Cantaro exists to be the user's canonical music and media memory.

For music, it syncs playlists and library state across connected platforms so years of collecting, favoriting, and maintaining monolith playlists can survive platform boundaries. Over time it should help users browse what they already own and turn broad collections into new themed playlists without losing the source archive.

For media, it helps users keep track of anime and related lists, import and update provider-backed progress, detect newly watched episodes through the browser extension, and discover new titles to add to watching, planned, or other personal lists.

Success looks like trust: a user can open Cantaro, understand what is synced, what needs review, and what has changed, then confidently let Cantaro update the right external places.

## Brand Personality

Calm, personal, interesting.

Cantaro should make a user's existing collection feel a little magical: not flashy magic, but the satisfying sense that years of saved music and watched stories have become organized, alive, and ready to be explored again. The voice should be warm and clear, with enough personality to make collecting feel exciting, while staying precise around sync, matching, provider state, and review workflows.

## Anti-references

Cantaro should not look or feel like YouTube, Spotify, AniList, Trakt, or any other connected platform clone. It should not feel cold, business-like, enterprise, workplace, workspace, gray, or boring.

Avoid generic SaaS dashboards, heavy corporate productivity metaphors, and UI that makes a personal library feel like an admin panel. Also avoid hiding complexity behind vague polish: sync state, unavailable tracks, provider conflicts, and media progress differences should be visible and understandable.

## Design Principles

1. Preserve the personal archive.
   The interface should treat playlists, libraries, watch history, and provider links as valuable long-lived collections, not disposable feeds.

2. Make sync feel trustworthy.
   Every sync, import, match, conflict, and provider update should communicate what happened, what will happen next, and what needs the user's attention.

3. Invite rediscovery.
   Browsing the library should make existing music and media feel worth returning to, with room for future themed playlist creation and discovery flows.

4. Stay task-first without becoming sterile.
   Cantaro is a tool, so workflows need to be efficient and predictable, but the atmosphere should remain warm, personal, and quietly expressive.

5. Be provider-independent.
   The product should feel like Cantaro owns the canonical experience. External platforms are connected endpoints, not the visual or interaction model.

## Accessibility & Inclusion

Use WCAG AA as the baseline. Maintain keyboard-friendly workflows, visible focus states, clear form labels, reduced-motion alternatives, and color-blind-safe status communication that does not rely on color alone. Motion should clarify state changes rather than decorate routine product work.
