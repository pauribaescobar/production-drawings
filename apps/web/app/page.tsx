"use client";

import { useState, type FormEvent } from "react";

type ApiError = {
  error: string;
  details?: string;
};

type PreflightReport = {
  orderNumber: string;
  matchedDfts: Array<{
    dftFileName: string;
    dftPath: string;
    quantity: string;
    material: string;
    treatment?: string | null;
    deliveryDate: string;
    dimensions: Record<string, string>;
  }>;
  missingDfts: Array<{
    dftFileName: string;
    dftPath: string;
  }>;
  readyForGeneration: boolean;
};

export default function Page() {
  const [orderPdf, setOrderPdf] = useState<File | null>(null);
  const [drawingsZip, setDrawingsZip] = useState<File | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [report, setReport] = useState<PreflightReport | null>(null);
  const [status, setStatus] = useState<string>(
    "Sube el PDF del pedido y el ZIP de dibujos para comprobar coincidencias.",
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
    setReport(null);
    setStatus("Comprobando coincidencias del pedido...");

    try {
      const response = await fetch("/api/generate?mode=preflight", {
        method: "POST",
        body: formData,
      });

      if (!response.ok) {
        const payload = (await response.json()) as ApiError;
        const message = payload.details
          ? `${payload.error} ${payload.details}`
          : payload.error;
        setError(message);
        setStatus("No se pudo generar el reporte de coincidencias.");
        return;
      }

      const payload = (await response.json()) as PreflightReport;
      setReport(payload);
      setStatus(
        payload.readyForGeneration
          ? "El pedido está listo para la generación final."
          : "Faltan coincidencias antes de generar el PDF final.",
      );
    } catch {
      setError("No se pudo conectar con la ruta de preflight.");
      setStatus("La comprobación falló antes de recibir el reporte.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <main className="page-shell">
      <section className="hero">
        <div className="eyebrow">production-drawings</div>
        <h1>Comprobar coincidencias del pedido</h1>
        <p>
          Sube el PDF del pedido y el ZIP de dibujos. El backend extrae los
          datos del pedido, analiza el contenido del ZIP y devuelve un reporte
          de coincidencias antes de pasar a la generación final.
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

        {report ? (
          <section className="response-panel" aria-live="polite">
            <div className="response-label">Reporte de preflight</div>
            <div className="summary-grid">
              <div className="summary-item">
                <span className="summary-key">orderNumber</span>
                <strong>{report.orderNumber}</strong>
              </div>
              <div className="summary-item">
                <span className="summary-key">readyForGeneration</span>
                <strong>{report.readyForGeneration ? "true" : "false"}</strong>
              </div>
              <div className="summary-item summary-item-wide">
                <span className="summary-key">matchedDfts</span>
                <strong>{report.matchedDfts.length}</strong>
              </div>
              <div className="summary-item summary-item-wide">
                <span className="summary-key">missingDfts</span>
                <strong>{report.missingDfts.length}</strong>
              </div>
            </div>
            <pre>{JSON.stringify(report, null, 2)}</pre>
          </section>
        ) : null}
      </section>

      <section className="footer-note">
        Un solo botón ejecuta la comprobación de pedido, ZIP y matching. La
        generación final sigue siendo el siguiente paso.
      </section>
    </main>
  );
}
