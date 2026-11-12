import { useId, type ReactNode, type RefObject } from "react";
import { ActionIcon } from "./ActionIcon";

export default function WorkspaceFilterDialog({ dialogRef, title, onOpen, children }: {
  dialogRef: RefObject<HTMLDialogElement | null>;
  title: string;
  onOpen?: () => void;
  children: ReactNode;
}) {
  const headingId = useId();
  return (
    <>
      <button type="button" data-variant="secondary" aria-haspopup="dialog" onClick={() => {
        onOpen?.();
        dialogRef.current?.showModal();
      }}><ActionIcon name="filter" />Filters</button>
      <dialog ref={dialogRef} className="workspace-filter-dialog workspace-panel" aria-labelledby={headingId}>
        <h2 id={headingId} className="mb-4 text-lg font-semibold">{title}</h2>
        {children}
      </dialog>
    </>
  );
}
