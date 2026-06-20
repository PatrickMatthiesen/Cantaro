import type { CSSProperties } from 'react';

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

  const selectedIndex = Math.max(0, options.findIndex((option) => option.value === value));
  const trackStyle: CSSProperties = {
    gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))`,
  };
  const indicatorStyle: CSSProperties = {
    width: `calc((100% - 0.5rem) / ${options.length})`,
    transform: `translateX(${selectedIndex * 100}%)`,
  };

  return (
    <div className={`segmented-switch flex items-center gap-2 rounded-3xl border border-white/80 bg-white/48 p-1.5 shadow-[0_14px_40px_rgba(88,74,150,0.08)] ${className}`}>
      {label ? (
        <span className="px-3 text-xs font-black tracking-[0.14em] text-slate-500 uppercase">{label}</span>
      ) : null}
      <div className="segmented-switch__track relative grid rounded-[1.35rem] bg-[#eeeaff]/82 p-1 shadow-[inset_0_0_0_1px_rgba(226,218,248,0.88)]" style={trackStyle}>
        <span
          className="segmented-switch__indicator absolute top-1 bottom-1 left-1 rounded-2xl bg-slate-950 shadow-[0_14px_34px_rgba(15,23,42,0.18)] transition-transform duration-300 ease-out"
          style={indicatorStyle}
          aria-hidden
        />
        {options.map((option) => {
          const isSelected = option.value === value;

          return (
            <button
              key={option.value}
              type="button"
              className={`segmented-switch__option relative z-10 inline-flex h-10 min-w-28 items-center justify-center rounded-2xl px-4 text-sm font-black transition-colors duration-200 ${
                isSelected ? 'segmented-switch__option--selected text-white' : 'text-slate-600 hover:text-slate-950'
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
