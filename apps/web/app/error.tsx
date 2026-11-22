"use client";
export default function ErrorPage({
  reset,
}: {
  error: Error;
  reset: () => void;
}) {
  return (
    <main id="main" className="wrap section">
      <h1>Couldn’t load this page</h1>
      <p>Please try again.</p>
      <button onClick={reset}>Retry</button>
    </main>
  );
}
