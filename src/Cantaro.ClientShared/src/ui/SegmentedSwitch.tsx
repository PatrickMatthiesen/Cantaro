export interface SegmentedSwitchOption<TValue extends string> {
  value: TValue;
  label: string;
}

export interface SegmentedSwitchProps<TValue extends string> {
  label?: string;
  value: TValue;
  options: ReadonlyArray<SegmentedSwitchOption<TValue>>;
  onChange: (value: TValue) => void;
  className?: string;
}

export function SegmentedSwitch<TValue extends string>({
  label,
  value,
  options,
  onChange,
  className = '',
}: SegmentedSwitchProps<TValue>) {
  if (options.length === 0) return null;

  return (
    <div className={`flex min-w-0 items-end gap-3 border-b border-border-subtle ${className}`}>
      {label ? <span className="shrink-0 py-3 text-xs font-semibold text-content-muted">{label}</span> : null}
      <div className="flex min-w-0 overflow-x-auto">
        {options.map((option) => {
          const isSelected = option.value === value;
          return (
            <button
              key={option.value}
              type="button"
              className={`min-h-11 shrink-0 border-b-2 px-4 text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-focus ${
                isSelected
                  ? 'border-personal-accent text-content'
                  : 'border-transparent text-content-muted hover:bg-surface-hover hover:text-content'
              }`}
              aria-pressed={isSelected}
              onClick={() => onChange(option.value)}
            >
              {option.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}
