import { syncApi, type MusicSyncJobResponse, type SyncStatusResponse } from '@cantaro/client-shared/music';
import { useCallback, useEffect, useMemo, useRef, useState, type Dispatch, type RefObject, type SetStateAction } from 'react';
import { requestPlaylistSyncDataRefresh } from './playlistSyncProgress';
import { importBlockReason, isActive, isPlaylistBusy, jobFailureMessage, pendingPlaylistIds, syncMappings, type SyncService } from './platformPlaylistSyncState';

type JobMap = Map<string, MusicSyncJobResponse>;
type JobSetter = Dispatch<SetStateAction<JobMap>>;
type ErrorSetter = Dispatch<SetStateAction<Record<string, string>>>;
type PendingSetter = Dispatch<SetStateAction<Set<string>>>;

const pollIntervalMs = 2000;
const maxPolls = 300;
const maxConsecutivePollErrors = 3;

function setPlaylistError(setErrors: ErrorSetter, playlistId: string, message: string | null) {
  setErrors((previous) => {
    const next = { ...previous };
    if (message) next[playlistId] = message;
    else delete next[playlistId];
    return next;
  });
}

function updateJob(setJobs: JobSetter, playlistId: string, job: MusicSyncJobResponse) {
  setJobs((previous) => new Map(previous).set(playlistId, job));
}

function errorText(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function useSyncStatus(service: SyncService) {
  const [status, setStatus] = useState<SyncStatusResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const requestNumber = useRef(0);
  const inFlight = useRef<{ service: SyncService; promise: Promise<void> } | null>(null);
  const invalidateRequest = useCallback(() => {
    requestNumber.current++;
    inFlight.current = null;
  }, []);

  const reload = useCallback(async () => {
    if (inFlight.current?.service === service) return inFlight.current.promise;
    const request = ++requestNumber.current;
    const promise = (async () => {
      setIsLoading(true);
      try {
        const response = await syncApi.getSyncStatus(service);
        if (request !== requestNumber.current) return;
        setStatus(response);
        setLoadError(null);
      } catch (error) {
        if (request !== requestNumber.current) return;
        setStatus(null);
        setLoadError(errorText(error, 'Could not load playlist sync status.'));
      } finally {
        if (request === requestNumber.current) setIsLoading(false);
      }
    })();
    inFlight.current = { service, promise };
    try {
      await promise;
    } finally {
      if (inFlight.current?.promise === promise) inFlight.current = null;
    }
  }, [service]);

  useEffect(() => {
    void reload();
    return invalidateRequest;
  }, [invalidateRequest, reload]);

  return { status, isLoading, loadError, reload };
}

interface PollingContext {
  jobsRef: RefObject<JobMap>;
  attemptsRef: RefObject<Map<string, number>>;
  failuresRef: RefObject<Map<string, number>>;
  inFlightRef: RefObject<Set<string>>;
  stoppedRef: RefObject<Set<string>>;
  setJobs: JobSetter;
  setErrors: ErrorSetter;
  reload: () => Promise<void>;
  isMounted: () => boolean;
}

function shouldPoll(job: MusicSyncJobResponse, context: PollingContext): boolean {
  return isActive(job) && !context.inFlightRef.current.has(job.id) && !context.stoppedRef.current.has(job.id);
}

async function recordPolledJob(context: PollingContext, playlistId: string, job: MusicSyncJobResponse) {
  if (!context.isMounted()) return;
  context.failuresRef.current.delete(job.id);
  if (!isActive(job)) {
    setPlaylistError(context.setErrors, playlistId, jobFailureMessage(job, playlistId));
    await context.reload();
    if (!context.isMounted()) return;
    requestPlaylistSyncDataRefresh();
  }
  context.jobsRef.current = new Map(context.jobsRef.current).set(playlistId, job);
  updateJob(context.setJobs, playlistId, job);
}

function recordPollFailure(context: PollingContext, playlistId: string, jobId: string, error: unknown) {
  if (!context.isMounted()) return;
  const failures = (context.failuresRef.current.get(jobId) ?? 0) + 1;
  context.failuresRef.current.set(jobId, failures);
  if (failures >= maxConsecutivePollErrors) {
    context.stoppedRef.current.add(jobId);
    setPlaylistError(context.setErrors, playlistId, errorText(error, 'Could not check sync progress.'));
  }
}

async function pollJob(context: PollingContext, playlistId: string, job: MusicSyncJobResponse) {
  if (!shouldPoll(job, context)) return;
  const attempts = (context.attemptsRef.current.get(job.id) ?? 0) + 1;
  context.attemptsRef.current.set(job.id, attempts);
  if (attempts > maxPolls) {
    context.stoppedRef.current.add(job.id);
    setPlaylistError(context.setErrors, playlistId, 'Sync is still running. Check Recent sync activity for updates.');
    return;
  }
  context.inFlightRef.current.add(job.id);
  try {
    await recordPolledJob(context, playlistId, await syncApi.getSyncJob(job.id));
  } catch (error) {
    recordPollFailure(context, playlistId, job.id, error);
  } finally {
    context.inFlightRef.current.delete(job.id);
  }
}

function useJobPolling(jobsRef: RefObject<JobMap>, setJobs: JobSetter, setErrors: ErrorSetter, reload: () => Promise<void>) {
  const attemptsRef = useRef(new Map<string, number>());
  const failuresRef = useRef(new Map<string, number>());
  const inFlightRef = useRef(new Set<string>());
  const stoppedRef = useRef(new Set<string>());

  useEffect(() => {
    let active = true;
    const context: PollingContext = {
      jobsRef, attemptsRef, failuresRef, inFlightRef, stoppedRef, setJobs, setErrors, reload,
      isMounted: () => active,
    };
    const timer = window.setInterval(() => {
      for (const [playlistId, job] of jobsRef.current.entries()) {
        void pollJob(context, playlistId, job);
      }
    }, pollIntervalMs);
    return () => { active = false; window.clearInterval(timer); };
  }, [jobsRef, reload, setErrors, setJobs]);

  return useCallback(() => {
    stoppedRef.current.clear();
    attemptsRef.current.clear();
    failuresRef.current.clear();
  }, []);
}

function setSubmittingPlaylist(submitting: Set<string>, setSubmitting: PendingSetter, playlistId: string, enabled: boolean) {
  if (enabled) submitting.add(playlistId);
  else submitting.delete(playlistId);
  setSubmitting(new Set(submitting));
}

export function usePlatformPlaylistSync(service: SyncService) {
  const { status, isLoading, loadError, reload: reloadStatus } = useSyncStatus(service);
  const [jobs, setJobs] = useState<JobMap>(() => new Map());
  const jobsRef = useRef<JobMap>(jobs);
  const [submitting, setSubmitting] = useState<Set<string>>(() => new Set());
  const submittingRef = useRef<Set<string>>(new Set());
  const [errors, setErrors] = useState<Record<string, string>>({});
  const resumePolling = useJobPolling(jobsRef, setJobs, setErrors, reloadStatus);
  const mappings = useMemo(() => syncMappings(status, service), [status, service]);
  const pending = useMemo(() => pendingPlaylistIds(submitting, jobs), [submitting, jobs]);

  const reload = useCallback(async () => {
    resumePolling();
    await reloadStatus();
  }, [reloadStatus, resumePolling]);

  const startSync = useCallback(async (playlistId: string) => {
    if (isPlaylistBusy(playlistId, submittingRef.current, jobsRef.current)) return;
    const blocked = importBlockReason(status, loadError, mappings.get(playlistId), isLoading);
    if (blocked) {
      setPlaylistError(setErrors, playlistId, blocked);
      return;
    }

    setSubmittingPlaylist(submittingRef.current, setSubmitting, playlistId, true);
    setPlaylistError(setErrors, playlistId, null);
    try {
      const job = await syncApi.createSyncJob({ service, servicePlaylistIds: [playlistId] });
      jobsRef.current = new Map(jobsRef.current).set(playlistId, job);
      updateJob(setJobs, playlistId, job);
    } catch (error) {
      setPlaylistError(setErrors, playlistId, errorText(error, 'Could not start playlist import.'));
    } finally {
      setSubmittingPlaylist(submittingRef.current, setSubmitting, playlistId, false);
    }
  }, [isLoading, loadError, mappings, service, status]);

  return { mappings, pending, errors, jobs, status, isLoading, loadError, reload, startSync };
}
