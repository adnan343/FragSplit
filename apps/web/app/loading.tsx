export default function Loading() {
  return (
    <main id="main" className="wrap section" aria-busy="true">
      <p role="status">Loading…</p>
      <div className="skeleton" />
    </main>
  );
}
