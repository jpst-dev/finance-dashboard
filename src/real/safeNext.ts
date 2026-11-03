const MAX_LENGTH = 2048;

/** Aceita apenas caminho interno: começa com uma única "/", sem "\", esquema nem caracteres de controle. */
export function safeNext(raw: unknown): string {
  if (typeof raw !== "string" || raw.length > MAX_LENGTH) return "/";
  if (!raw.startsWith("/") || raw.startsWith("//")) return "/";
  if (raw.includes("\\")) return "/";
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f-\u009f]/.test(raw)) return "/";
  return raw;
}
