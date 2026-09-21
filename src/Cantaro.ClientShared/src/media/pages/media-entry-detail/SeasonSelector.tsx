import type { SeasonOption, SeasonSelection } from "./seasonEpisodes";

export function hasSeasonChoices(options: readonly SeasonOption[]) {
  return options.some(option => option.value === "specials")
    || options.filter(option => typeof option.value === "number").length > 1;
}

export function SeasonSelector({
  options,
  value,
  onChange,
  label = "Season",
}: {
  options: readonly SeasonOption[];
  value: SeasonSelection;
  onChange: (selection: SeasonSelection) => void;
  label?: string;
}) {
  if (!hasSeasonChoices(options)) return null;
  return (
    <select
      aria-label={label}
      value={typeof value === "number" ? `season:${value}` : value}
      onChange={(event) => {
        const selected = event.target.value;
        onChange(selected.startsWith("season:")
          ? Number(selected.slice("season:".length))
          : selected as SeasonSelection);
      }}
      className="min-h-9 w-fit cursor-pointer border border-transparent bg-surface px-2 text-sm font-semibold text-content hover:border-border-strong focus-visible:outline-2 focus-visible:outline-focus"
    >
      {options.map((option) => (
        <option
          key={option.value}
          value={typeof option.value === "number" ? `season:${option.value}` : option.value}
        >
          {option.label}
        </option>
      ))}
    </select>
  );
}
