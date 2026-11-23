export const aioStreamsPairingEndpoint = '/api/media/aiostreams-pairing';

export async function getAioStreamsPairingStatus(signal: AbortSignal) {
  const response = await fetch(aioStreamsPairingEndpoint, { signal });
  if (!response.ok) throw new Error('Could not load the AIOStreams connection.');
  return await response.json() as { paired: boolean; playbackInstanceUrl?: string | null };
}
