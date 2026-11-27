import type { ReactNode } from "react";
import { Link } from "react-router";
import { ActionIcon } from "../../../components/ActionIcon";

export default function PatientDetailsPanel({ title, backTo, backLabel, children }: {
  title: string; backTo: string; backLabel: string; children: ReactNode;
}) {
  return (
    <div className="mx-auto w-full max-w-4xl space-y-3">
      <Link to={backTo} className="workspace-button" data-variant="secondary">
        <ActionIcon name="back" />
        {backLabel}
      </Link>
      <section className="workspace-panel space-y-4" aria-labelledby="patient-record-heading">
        <h1 id="patient-record-heading" className="page-title">{title}</h1>
        {children}
      </section>
    </div>
  );
}
