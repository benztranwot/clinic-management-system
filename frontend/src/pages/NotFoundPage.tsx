import { Link } from "react-router";
import { ActionIcon } from "../components/ActionIcon";

export default function NotFoundPage({ homePath }: { homePath: string }) {
  return (
    <section className="workspace-panel mx-auto max-w-lg space-y-4 text-center" aria-labelledby="not-found-heading">
      <h1 id="not-found-heading" className="page-title">Page not found</h1>
      <p className="text-subtle">Check the address or return to your workspace.</p>
      <Link to={homePath} className="workspace-button"><ActionIcon name="home" />Return to workspace</Link>
    </section>
  );
}
