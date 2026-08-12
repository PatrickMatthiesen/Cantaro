import type {
  MediaFranchiseGraphDto,
  MediaFranchiseNodeDto,
  MediaFranchiseRelationDto,
} from '../../services/mediaApi';

export interface FranchiseBranch {
  node: MediaFranchiseNodeDto;
  relation: MediaFranchiseRelationDto;
  displayRelationType: string;
}

export interface FranchiseBranchGroup {
  source: MediaFranchiseNodeDto;
  branches: FranchiseBranch[];
}

export interface FranchisePresentation {
  continuityNodes: MediaFranchiseNodeDto[];
  branchGroups: FranchiseBranchGroup[];
  relatedTitleCount: number;
}

function unique<T>(values: T[]): T[] {
  return [...new Set(values)];
}

function branchSortKey(branch: FranchiseBranch): string {
  return `${relationLabel(branch.displayRelationType)}:${branch.node.startYear ?? 9999}:${branch.node.canonicalTitle}:${branch.node.mediaTitleId}`;
}

export function relationLabel(relationType: string): string {
  const normalized = relationType.trim().toLowerCase().replaceAll('-', '_').replaceAll(' ', '_');
  const labels: Record<string, string> = {
    adaptation: 'Adaptation',
    alternative: 'Alternative',
    character: 'Character story',
    compilation: 'Compilation',
    contains: 'Contains',
    other: 'Related',
    parent: 'Parent story',
    prequel: 'Prequel',
    sequel: 'Sequel',
    side_story: 'Side story',
    source: 'Source material',
    spin_off: 'Spin-off',
    summary: 'Summary',
  };

  return labels[normalized]
    ?? (normalized.split('_').filter(Boolean).map((part) => `${part[0]?.toUpperCase() ?? ''}${part.slice(1)}`).join(' ')
      || 'Related');
}

export function continuityRelationLabel(
  graph: MediaFranchiseGraphDto,
  sourceMediaTitleId: string,
  targetMediaTitleId: string,
): string {
  const relation = graph.relations.find((candidate) =>
    candidate.isEpisodeContinuity
    && candidate.sourceMediaTitleId === sourceMediaTitleId
    && candidate.targetMediaTitleId === targetMediaTitleId);

  if (relation) {
    return relation.relationType === 'prequel' ? 'Prequel' : 'Sequel';
  }

  const reverseRelation = graph.relations.find((candidate) =>
    candidate.isEpisodeContinuity
    && candidate.sourceMediaTitleId === targetMediaTitleId
    && candidate.targetMediaTitleId === sourceMediaTitleId);

  return reverseRelation ? 'Sequel' : 'Next';
}

function buildContinuity(
  graph: MediaFranchiseGraphDto,
  nodesById: Map<string, MediaFranchiseNodeDto>,
) {
  const currentNode = nodesById.get(graph.currentMediaTitleId)
    ?? graph.nodes.find((node) => node.isCurrent);
  const orderedIds = unique(graph.continuity.orderedMediaTitleIds)
    .filter((mediaTitleId) => nodesById.has(mediaTitleId));

  if (currentNode && !orderedIds.includes(currentNode.mediaTitleId)) {
    orderedIds.push(currentNode.mediaTitleId);
  }

  const continuityNodes = orderedIds.map((mediaTitleId) => nodesById.get(mediaTitleId)!);
  return { currentNode, orderedIds, continuityNodes };
}

function addRelationBranch(
  branchesBySource: Map<string, FranchiseBranch[]>,
  representedBranchIds: Set<string>,
  source: MediaFranchiseNodeDto,
  target: MediaFranchiseNodeDto,
  relation: MediaFranchiseRelationDto,
  displayRelationType = relation.relationType,
) {
  const branches = branchesBySource.get(source.mediaTitleId) ?? [];
  const isDuplicate = branches.some((branch) =>
    branch.node.mediaTitleId === target.mediaTitleId
    && branch.displayRelationType === displayRelationType);
  if (isDuplicate) return;

  branches.push({ node: target, relation, displayRelationType });
  representedBranchIds.add(target.mediaTitleId);
  branchesBySource.set(source.mediaTitleId, branches);
}

function collectRelationBranches(
  graph: MediaFranchiseGraphDto,
  nodesById: Map<string, MediaFranchiseNodeDto>,
  continuityNodes: MediaFranchiseNodeDto[],
) {
  const representedBranchIds = new Set<string>();
  const branchesBySource = new Map<string, FranchiseBranch[]>();
  const laneEdges = new Set(
    continuityNodes.slice(0, -1).map((node, index) =>
      [node.mediaTitleId, continuityNodes[index + 1].mediaTitleId].sort().join(':')),
  );
  const continuityIds = new Set(continuityNodes.map((node) => node.mediaTitleId));

  for (const relation of graph.relations) {
    if (isLaneRelation(relation, laneEdges)) continue;
    const endpoints = getRelationEndpoints(relation, nodesById, continuityIds);
    if (!endpoints) continue;
    const { source, target, displayRelationType } = endpoints;
    addRelationBranch(
      branchesBySource,
      representedBranchIds,
      source,
      target,
      relation,
      displayRelationType,
    );
  }

  return { representedBranchIds, branchesBySource };
}

function isLaneRelation(relation: MediaFranchiseRelationDto, laneEdges: Set<string>): boolean {
  const relationKey = [relation.sourceMediaTitleId, relation.targetMediaTitleId].sort().join(':');
  return relation.isEpisodeContinuity && laneEdges.has(relationKey);
}

function getRelationEndpoints(
  relation: MediaFranchiseRelationDto,
  nodesById: Map<string, MediaFranchiseNodeDto>,
  continuityIds: Set<string>,
): {
  source: MediaFranchiseNodeDto;
  target: MediaFranchiseNodeDto;
  displayRelationType: string;
} | undefined {
  const source = nodesById.get(relation.sourceMediaTitleId);
  const target = nodesById.get(relation.targetMediaTitleId);
  if (!source || !target || source.mediaTitleId === target.mediaTitleId) return undefined;
  if (!continuityIds.has(source.mediaTitleId) && continuityIds.has(target.mediaTitleId)) {
    return {
      source: target,
      target: source,
      displayRelationType: invertRelationType(relation.relationType),
    };
  }
  return { source, target, displayRelationType: relation.relationType };
}

const inverseRelationTypes = new Map<string, string>([
  ['prequel', 'sequel'],
  ['sequel', 'prequel'],
  ['adaptation', 'source'],
  ['source', 'adaptation'],
  ['compilation', 'contains'],
  ['contains', 'compilation'],
]);

function invertRelationType(relationType: string): string {
  return inverseRelationTypes.get(relationType.trim().toLowerCase()) ?? relationType;
}

function addUnconnectedBranches(
  graph: MediaFranchiseGraphDto,
  continuityNodes: MediaFranchiseNodeDto[],
  fallbackSource: MediaFranchiseNodeDto | undefined,
  representedBranchIds: Set<string>,
  branchesBySource: Map<string, FranchiseBranch[]>,
) {
  if (!fallbackSource) return;

  const continuityIds = new Set(continuityNodes.map((node) => node.mediaTitleId));
  const unconnected = graph.nodes.filter((node) =>
    !continuityIds.has(node.mediaTitleId)
    && !representedBranchIds.has(node.mediaTitleId)
    && node.mediaTitleId !== fallbackSource.mediaTitleId);
  for (const node of unconnected) {
    addRelationBranch(branchesBySource, representedBranchIds, fallbackSource, node, {
      sourceMediaTitleId: fallbackSource.mediaTitleId,
      targetMediaTitleId: node.mediaTitleId,
      relationType: 'other',
      isEpisodeContinuity: false,
    });
  }
}

function buildBranchGroups(
  branchesBySource: Map<string, FranchiseBranch[]>,
  nodesById: Map<string, MediaFranchiseNodeDto>,
  orderedIds: string[],
) {
  return [...branchesBySource.entries()]
    .map(([sourceId, branches]) => ({
      source: nodesById.get(sourceId)!,
      branches: branches.sort((left, right) =>
        branchSortKey(left).localeCompare(branchSortKey(right))),
    }))
    .filter((group) => group.source && group.branches.length > 0)
    .sort((left, right) => {
      const leftIndex = orderedIds.indexOf(left.source.mediaTitleId);
      const rightIndex = orderedIds.indexOf(right.source.mediaTitleId);
      return (leftIndex < 0 ? Number.MAX_SAFE_INTEGER : leftIndex)
        - (rightIndex < 0 ? Number.MAX_SAFE_INTEGER : rightIndex);
    });
}

export function buildFranchisePresentation(graph: MediaFranchiseGraphDto): FranchisePresentation {
  const nodesById = new Map(graph.nodes.map((node) => [node.mediaTitleId, node]));
  const { currentNode, orderedIds, continuityNodes } = buildContinuity(graph, nodesById);
  const { representedBranchIds, branchesBySource } = collectRelationBranches(
    graph,
    nodesById,
    continuityNodes,
  );
  addUnconnectedBranches(
    graph,
    continuityNodes,
    currentNode ?? continuityNodes[0],
    representedBranchIds,
    branchesBySource,
  );
  const branchGroups = buildBranchGroups(branchesBySource, nodesById, orderedIds);

  return {
    continuityNodes,
    branchGroups,
    relatedTitleCount: new Set(
      branchGroups.flatMap((group) => group.branches.map((branch) => branch.node.mediaTitleId)),
    ).size,
  };
}
