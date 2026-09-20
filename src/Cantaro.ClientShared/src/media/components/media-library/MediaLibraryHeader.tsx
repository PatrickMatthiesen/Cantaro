export function MediaLibraryRefreshErrorNotice({ error }: { error: string | null }) {
    if (!error) {
        return null;
    }

    return (
        <div className="border-b border-border-subtle py-2" role="status">
            <p className="text-sm text-content-muted">{error}</p>
        </div>
    );
}
