import { describe, expect, it } from 'bun:test';
import type {
  MediaFranchiseGraphDto,
  MediaFranchiseNodeDto,
  MediaFranchiseRelationDto,
} from '../../Cantaro.ClientShared/src/media/services/mediaApi';
import {
  buildFranchisePresentation,
  continuityRelationLabel,
  relationLabel,
} from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/franchiseGraph';

function node(
  mediaTitleId: string,
  overrides: Partial<MediaFranchiseNodeDto> = {},
): MediaFranchiseNodeDto {
  return {
    mediaTitleId,
    provider: 'anilist',
    providerMediaId: mediaTitleId,
    canonicalTitle: mediaTitleId,
    mediaKind: 'anime',
    isCurrent: false,
    ...overrides,
  };
}

function relation(
  sourceMediaTitleId: string,
  targetMediaTitleId: string,
  relationType: string,
  isEpisodeContinuity: boolean,
): MediaFranchiseRelationDto {
  return { sourceMediaTitleId, targetMediaTitleId, relationType, isEpisodeContinuity };
}

function graph(overrides: Partial<MediaFranchiseGraphDto> = {}): MediaFranchiseGraphDto {
  return {
    currentMediaTitleId: 'season-2',
    sourceProvider: 'anilist',
    nodes: [
      node('season-1'),
      node('season-2', { isCurrent: true }),
      node('season-3'),
    ],
    relations: [
      relation('season-1', 'season-2', 'SEQUEL', true),
      relation('season-2', 'season-3', 'SEQUEL', true),
    ],
    continuity: {
      orderedMediaTitleIds: ['season-1', 'season-2', 'season-3'],
      episodeOffsetByMediaTitleId: { 'season-1': 0, 'season-2': 12, 'season-3': 24 },
      isComplete: true,
    },
    ...overrides,
  };
}

describe('franchise graph presentation', () => {
  it('uses the backend continuity order and preserves the current node', () => {
    const presentation = buildFranchisePresentation(graph());

    expect(presentation.continuityNodes.map((item) => item.mediaTitleId)).toEqual([
      'season-1',
      'season-2',
      'season-3',
    ]);
    expect(presentation.continuityNodes.find((item) => item.isCurrent)?.mediaTitleId).toBe('season-2');
  });

  it('keeps side stories out of the episode continuity lane', () => {
    const value = graph({
      nodes: [...graph().nodes, node('ova', { canonicalTitle: 'OVA' })],
      relations: [...graph().relations, relation('season-2', 'ova', 'SIDE_STORY', false)],
    });
    const presentation = buildFranchisePresentation(value);

    expect(presentation.continuityNodes.map((item) => item.mediaTitleId)).not.toContain('ova');
    expect(presentation.branchGroups[0].branches[0].node.mediaTitleId).toBe('ova');
    expect(presentation.relatedTitleCount).toBe(1);
  });

  it('deduplicates repeated continuity ids and appends a missing current node', () => {
    const value = graph({
      continuity: {
        orderedMediaTitleIds: ['season-1', 'season-1'],
        episodeOffsetByMediaTitleId: { 'season-1': 0 },
        isComplete: false,
      },
    });

    expect(buildFranchisePresentation(value).continuityNodes.map((item) => item.mediaTitleId)).toEqual([
      'season-1',
      'season-2',
    ]);
  });

  it('sorts branches consistently by relation, year, and title', () => {
    const value = graph({
      nodes: [
        ...graph().nodes,
        node('later-side-story', { canonicalTitle: 'Beta', startYear: 2025 }),
        node('earlier-side-story', { canonicalTitle: 'Alpha', startYear: 2024 }),
        node('adaptation', { canonicalTitle: 'Manga' }),
      ],
      relations: [
        ...graph().relations,
        relation('season-2', 'later-side-story', 'SIDE_STORY', false),
        relation('season-2', 'earlier-side-story', 'SIDE_STORY', false),
        relation('season-2', 'adaptation', 'ADAPTATION', false),
      ],
    });

    expect(buildFranchisePresentation(value).branchGroups[0].branches.map((branch) => branch.node.mediaTitleId)).toEqual([
      'adaptation',
      'earlier-side-story',
      'later-side-story',
    ]);
  });

  it('falls back safely for unknown relation labels', () => {
    expect(relationLabel('SIDE_STORY')).toBe('Side story');
    expect(relationLabel('FUTURE_PROJECT')).toBe('Future Project');
    expect(continuityRelationLabel(graph(), 'season-3', 'missing')).toBe('Next');
  });

  it('labels an ordered transition from a reverse prequel assertion as a sequel', () => {
    const value = graph({
      relations: [relation('season-2', 'season-1', 'prequel', true)],
    });

    expect(continuityRelationLabel(value, 'season-1', 'season-2')).toBe('Sequel');
  });
});
