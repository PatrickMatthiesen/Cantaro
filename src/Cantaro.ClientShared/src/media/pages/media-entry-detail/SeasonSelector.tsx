import { SelectField } from "../../../ui";
import type { SeasonOption, SeasonSelection } from "./seasonEpisodes";

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
  if (options.length < 2) return null;
  return (
    <SelectField
      label={label}
      value={typeof value === "number" ? `season:${value}` : value}
      onChange={(event) => {
        const selected = event.target.value;
        onChange(selected.startsWith("season:")
          ? Number(selected.slice("season:".length))
          : selected as SeasonSelection);
      }}
      containerClassName="min-w-40 w-fit"
    >
      {options.map((option) => (
        <option
          key={option.value}
          value={typeof option.value === "number" ? `season:${option.value}` : option.value}
        >
          {option.label}
        </option>
      ))}
    </SelectField>
  );
}
