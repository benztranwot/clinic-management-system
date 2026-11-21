import { useRef } from "react";
import WorkspaceFilterDialog from "./WorkspaceFilterDialog";
import { ActionIcon } from "./ActionIcon";

export default function DirectoryFilters<T extends Record<string, string>>({ title, fields, filters, onApply }: {
  title: string; fields: readonly (readonly [keyof T & string, string, ("date" | "search")?])[];
  filters: T; onApply: (filters: T) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  return <WorkspaceFilterDialog dialogRef={dialog} title={title} onOpen={() => dialog.current?.querySelector("form")?.reset()}>
    <form key={JSON.stringify(filters)} className="space-y-4" onSubmit={event => {
      event.preventDefault();
      const data = new FormData(event.currentTarget);
      onApply(Object.fromEntries(fields.map(([name]) => [name, String(data.get(name) ?? "").trim()])) as T);
      dialog.current?.close();
    }}>
      <div className="grid gap-3 sm:grid-cols-2">
        {fields.map(([name, label, type = "search"]) => <label key={name} className="grid gap-1 text-sm font-medium">
          {label}<input name={name} type={type} maxLength={200} defaultValue={filters[name]}
            className="min-w-0 rounded-md border border-panel-border px-3 py-2" />
        </label>)}
      </div>
      <div className="flex flex-wrap gap-2">
        <button type="submit"><ActionIcon name="check" />Apply filters</button>
        <button type="button" data-variant="secondary" onClick={() => {
          onApply(Object.fromEntries(fields.map(([name]) => [name, ""])) as T); dialog.current?.close();
        }}><ActionIcon name="reset" />Clear filters</button>
        <button type="button" data-variant="secondary" onClick={() => dialog.current?.close()}><ActionIcon name="close" />Close</button>
      </div>
    </form>
  </WorkspaceFilterDialog>;
}
