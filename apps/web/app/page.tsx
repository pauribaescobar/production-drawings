"use client";

import { useState, type FormEvent } from "react";

type ApiError = {
  error: string;
  details?: string;
};

export default function Page() {
  const [orderPdf, setOrderPdf] = useState<File | null>(null);
  const [drawingsZip, setDrawingsZip] = useState<File | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string>(
    "Sube el PDF del pedido y el ZIP de dibujos para generar el PDF final.",
  );

  const ready = Boolean(orderPdf && drawingsZip);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!ready || isSubmitting || !orderPdf || !drawingsZip) {
      return;
    }

    const formData = new FormData();
    formData.set("pdf", orderPdf);
    formData.set("zip", drawingsZip);

    setIsSubmitting(true);
    setError(null);
    setStatus("Generando el PDF final...");

    try {
      const response = await fetch("/api/generate", {
        method: "POST",
        body: formData,
      });

      if (!response.ok) {
        let message = "No se pudo generar el PDF final.";
        try {
          const payload = (await response.json()) as ApiError;
          message = payload.details ? `${payload.error} ${payload.details}` : payload.error;
        } catch {
          message = `No se pudo generar el PDF final. HTTP ${response.status}.`;
        }
        setError(message);
        setStatus("La generación falló antes de recibir el PDF.");
        return;
      }

      const pdfBlob = await response.blob();
      const pdfUrl = URL.createObjectURL(pdfBlob);
      window.open(pdfUrl, "_blank", "noopener,noreferrer");
      setStatus("PDF final generado y abierto en una nueva pestaña.");
    } catch {
      setError("No se pudo conectar con la ruta de generación.");
      setStatus("La generación falló antes de abrir el PDF.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <main className="page-shell">
      <section className="hero">
        <div className="eyebrow">production-drawings</div>
        <h1>Generar planos nuevos</h1>
        <p>
          Sube el PDF del pedido y el ZIP de dibujos. El backend extrae los
          datos del pedido, analiza el contenido del ZIP y devuelve el PDF
          final directamente.
        </p>
      </section>

      <section className="panel">
        <form onSubmit={handleSubmit}>
          <div className="upload-grid">
            <label className="dropzone">
              <span className="label">PDF del pedido</span>
              <span className="hint">Obligatorio</span>
              <input
                type="file"
                accept="application/pdf,.pdf"
                onChange={(event) => setOrderPdf(event.target.files?.[0] ?? null)}
              />
              <strong>{orderPdf ? orderPdf.name : "Seleccionar archivo"}</strong>
            </label>

            <label className="dropzone">
              <span className="label">ZIP de dibujos</span>
              <span className="hint">Obligatorio</span>
              <input
                type="file"
                accept=".zip,application/zip"
                onChange={(event) => setDrawingsZip(event.target.files?.[0] ?? null)}
              />
              <strong>{drawingsZip ? drawingsZip.name : "Seleccionar archivo"}</strong>
            </label>
          </div>

          <div className="status-row">
            <div className="status-copy">
              <span className={ready ? "status-dot ready" : "status-dot"} />
              <span>{status}</span>
            </div>

            <button className="generate-button" type="submit" disabled={!ready || isSubmitting}>
              {isSubmitting ? "Comprobando..." : "Generar planos nuevos"}
            </button>
          </div>
        </form>
        {error ? (
          <section className="response-panel response-panel-error" aria-live="polite">
            <div className="response-label">Error</div>
            <pre>{error}</pre>
          </section>
        ) : null}
      </section>

      <section className="footer-note">
        Un solo botón ejecuta la generación final y abre el PDF resultante en una nueva pestaña.
      </section>
    </main>
  );
}
