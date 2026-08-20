export function MediaLibraryRefreshErrorNotice({ error }: { error: string | null }) {
    if (!error) {
        return null;
    }

    return (
        <div className="border-y border-danger-border bg-danger-surface px-4 py-3" role="status">
            <p className="text-sm font-medium text-danger-content">{error}</p>
        </div>
    );
}
