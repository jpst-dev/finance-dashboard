import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises } from "@vue/test-utils";
import { createMemoryHistory, type Router } from "vue-router";
import type { App } from "vue";
import i18n from "../real/i18n";
import { getApi, setApi } from "../real/api/context";
import { setRouter } from "../real/routerRef";
import { setAppConfig } from "../real/appConfig";
import { bootDemo, createDemoApp, DEMO_BUNDLE_MARKER } from "./boot";
import { DEMO_FIXTURES as f } from "./fixtures";

// Gráficos dependem de canvas (inexistente no jsdom); o resto da interface é real.
vi.mock("vue-chartjs", async () => {
  const { defineComponent, h } = await import("vue");
  const make = (name: string) =>
    defineComponent({
      name,
      props: ["data", "options"],
      setup(_, { expose }) {
        expose({ chart: { update() {} } });
        return () => h("canvas");
      },
    });
  return { Bar: make("bar"), Doughnut: make("doughnut") };
});

const PT_READ_ONLY = "Demonstração somente leitura: alterações não são salvas.";
const EN_READ_ONLY = "Read-only demo: changes are not saved.";
const MONEY_LIKE = /\d[.,]\d{2}/; // sem lookahead: textContent concatena nós sem separador ("75,4013/10")

let fetchSpy: ReturnType<typeof vi.fn>;
let xhrOpenSpy: ReturnType<typeof vi.spyOn>;
let xhrSendSpy: ReturnType<typeof vi.spyOn>;
let mounted: { app: App; host: HTMLElement } | null = null;

async function settle() {
  for (let i = 0; i < 4; i++) await flushPromises();
}

async function boot(): Promise<{ router: Router }> {
  const { app, router } = await createDemoApp(createMemoryHistory());
  const host = document.createElement("div");
  document.body.appendChild(host);
  app.mount(host);
  mounted = { app, host };
  await settle();
  return { router };
}

const q = <T extends Element = HTMLElement>(sel: string) => document.querySelector<T>(sel);
const qa = <T extends Element = HTMLElement>(sel: string) => Array.from(document.querySelectorAll<T>(sel));
const bodyText = () => document.body.textContent ?? "";

async function visit(router: Router, path: string) {
  await router.push(path);
  await settle();
}
async function click(el: Element | null) {
  expect(el).not.toBeNull();
  (el as HTMLElement).click();
  await settle();
}
async function setValue(el: Element | null, value: string, event = "change") {
  expect(el).not.toBeNull();
  (el as HTMLInputElement | HTMLSelectElement).value = value;
  el!.dispatchEvent(new Event(event, { bubbles: true }));
  await settle();
}
async function submit(form: Element | null) {
  expect(form).not.toBeNull();
  form!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  await settle();
}

function expectNoMoney() {
  const haystacks: string[] = [bodyText(), document.title];
  for (const el of qa("*")) for (const a of Array.from(el.attributes)) haystacks.push(a.value);
  for (const h of haystacks) expect(h).not.toMatch(MONEY_LIKE);
}

/** Envia um arquivo "extrato.ofx" cuja leitura lança; devolve os espiões de leitura (nenhum deve ser chamado). */
async function uploadExample() {
    const file = new File(["conteudo"], "extrato.ofx");
    const boom = () => {
      throw new Error("leitura proibida");
    };
    // jsdom nem sempre define estes métodos no File: instala espiões que lançam, existam ou não.
    const fileReads = ["arrayBuffer", "text", "stream", "slice"].map((name) => {
      const spy = vi.fn(boom);
      Object.defineProperty(file, name, { value: spy, configurable: true });
      return spy;
    });
    const reads = [
      ...fileReads,
      vi.spyOn(FileReader.prototype, "readAsArrayBuffer").mockImplementation(boom),
      vi.spyOn(FileReader.prototype, "readAsText").mockImplementation(boom),
      vi.spyOn(FileReader.prototype, "readAsDataURL").mockImplementation(boom),
    ];

    await setValue(q("[data-test=account]"), CHECKING.id);
    const input = q<HTMLInputElement>("input[type=file]")!;
    Object.defineProperty(input, "files", { value: [file], configurable: true });
    await setValue(input, "", "change");
    await submit(q("[data-test=upload-form]"));
  return reads;
}

// A fila de pendências não traz linhas já num vínculo confirmado.
const PENDING = f.transactions.filter((t) => t.state === "PendingClassification" && t.link?.state !== "Confirmed");
const CHECKING = f.accounts.find((a) => a.type === "Checking")!;
const PREVIEW = f.importDetails[f.meta.previewBatchId];

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2027-03-01T12:00:00"));
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
  document.title = "";
  document.documentElement.removeAttribute(DEMO_BUNDLE_MARKER);
  fetchSpy = vi.fn();
  vi.stubGlobal("fetch", fetchSpy);
  xhrOpenSpy = vi.spyOn(XMLHttpRequest.prototype, "open");
  xhrSendSpy = vi.spyOn(XMLHttpRequest.prototype, "send");
});

afterEach(() => {
  try {
    expect(fetchSpy).not.toHaveBeenCalled();
    expect(xhrOpenSpy).not.toHaveBeenCalled();
    expect(xhrSendSpy).not.toHaveBeenCalled();
  } finally {
    mounted?.app.unmount();
    mounted?.host.remove();
    mounted = null;
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    vi.useRealTimers();
    setApi(null);
    setRouter(null);
    setAppConfig(null);
    localStorage.clear();
  }
});

describe("demo estático: interface real somente leitura", () => {
  it("boots_without_login_on_reference_month", async () => {
    const { router } = await boot();
    expect(router.currentRoute.value.path).toBe("/");
    expect(q("[data-test=month-label]")!.textContent).toBe("outubro de 2026");
    expect(q<HTMLButtonElement>("[data-test=month-next]")!.disabled).toBe(true);
    expect(q("[data-testid=demo-banner]")).not.toBeNull();
    expect(q("[data-testid=logout]")).toBeNull();
    expect(document.documentElement.getAttribute(DEMO_BUNDLE_MARKER)).toBe("1");
  });

  it("system_clock_in_the_past_still_opens_reference_month", async () => {
    vi.setSystemTime(new Date("2026-01-01T12:00:00"));
    await boot();
    expect(q("[data-test=month-label]")!.textContent).toBe("outubro de 2026");
  });

  it("login_route_redirects_to_overview", async () => {
    const { router } = await boot();
    await visit(router, "/login");
    expect(router.currentRoute.value.path).toBe("/");
    expect(q("[data-test=month-label]")).not.toBeNull();
  });

  it("visits_every_page_without_network", async () => {
    const { router } = await boot();
    const expectations: [string, string][] = [
      ["/", f.accounts[0].name],
      ["/transactions", f.transactions[0].description],
      ["/pending", PENDING[0].description],
      ["/accounts", f.accounts[0].name],
      ["/import", CHECKING.name],
    ];
    for (const [path, expected] of expectations) {
      await visit(router, path);
      expect(router.currentRoute.value.path).toBe(path);
      expect(bodyText(), path).toContain(expected);
      expect(q("[data-testid=pending-badge]")!.textContent, path).toMatch(new RegExp(`^\\s*${PENDING.length}(?!\\d)`));
    }
  });

  it("pending_shows_suggestions_and_linked_sections_without_network", async () => {
    const { router } = await boot();
    await visit(router, "/pending");
    expect(q("[data-test=suggestions]")).not.toBeNull();
    expect(qa("[data-test=suggestion]")).toHaveLength(f.links.suggested.length);
    expect(q("[data-test=linked-pairs]")).not.toBeNull();
    expect(qa("[data-test=linked]")).toHaveLength(f.links.confirmed.length);
    for (const l of [...f.links.suggested, ...f.links.confirmed]) expect(bodyText()).toContain(l.out!.description);
    // As ações são somente leitura (o afterEach confirma que nada foi à rede).
    await click(q("[data-test=link-confirm]"));
    expect(q("[data-test=error]")!.textContent).toBe(PT_READ_ONLY);
  });

  it("month_without_movement_shows_empty_state_not_error", async () => {
    await boot();
    // A interface real não tem [data-test=empty-state] por mês (só quando não há nenhum lançamento):
    // o mês vazio mostra os cartões zerados e "category-empty", sem erro.
    for (let i = 0; i < 6; i++) await click(q("[data-test=month-prev]"));
    expect(q("[data-test=month-label]")!.textContent).toBe("abril de 2026");
    expect(q("[data-test=summary-cards]")).not.toBeNull();
    expect(q("[data-test=category-empty]")).not.toBeNull();
    expect(q("[data-test=summary-error]")).toBeNull();
    expect(qa("[role=alert]")).toHaveLength(0);
  });

  it("import_shows_example_preview_without_reading_file", async () => {
    const { router } = await boot();
    await visit(router, "/import");

    const reads = await uploadExample();

    expect(q("[role=tablist]")).not.toBeNull();
    const text = bodyText();
    expect(PREVIEW.lines).toHaveLength(6);
    for (const l of PREVIEW.lines) expect(text).toContain(l.description);

    await click(q("[data-test=confirm]"));
    expect(q("[data-test=error]")!.textContent).toBe(PT_READ_ONLY);
    for (const r of reads) expect(r).not.toHaveBeenCalled();
  });

  it("mutation_shows_translated_read_only_message", async () => {
    const { router } = await boot();

    async function tryMutations(expected: string) {
      await visit(router, "/pending");
      const sel = q<HTMLSelectElement>(`#pc-${PENDING[0].id}`)!;
      const option = Array.from(sel.options).find((o) => o.value !== "")!;
      await setValue(sel, option.value);
      expect(q("[data-test=error]")!.textContent, "pending").toBe(expected);
      expect(qa("[data-test=pending-row]"), "pending rows").toHaveLength(PENDING.length);
      expect(bodyText()).toContain(PENDING[0].description);
      expect(q("[data-testid=pending-badge]")!.textContent).toMatch(new RegExp(String.raw`^\s*${PENDING.length}(?!\d)`));

      await visit(router, "/transactions");
      await click(q("[data-test=new-tx]"));
      await setValue(q("#tx-amount"), "12,00", "input");
      await setValue(q("#tx-description"), "Teste somente leitura", "input");
      await submit(q("[data-test=tx-form]"));
      expect(q("[data-test=error]")!.textContent, "create").toBe(expected);
      expect(qa("[data-test=tx-row]"), "tx rows").toHaveLength(Math.min(50, f.transactions.length));
      expect(bodyText()).toContain(f.transactions[0].description);
      expect(bodyText()).not.toContain("Teste somente leitura");
    }

    await tryMutations(PT_READ_ONLY);
    await click(q("button[lang=en-US]"));
    await tryMutations(EN_READ_ONLY);

    const api = getApi();
    expect((await api.transactions.list({ size: 200 })).items).toEqual(f.transactions);
    expect(await api.accounts.list()).toEqual(f.accounts);
  });

  it("hide_values_masks_every_page", async () => {
    const { router } = await boot();
    const anchors: [string, string][] = [
      ["/", f.accounts[0].name],
      ["/transactions", f.transactions[0].description],
      ["/pending", PENDING[0].description],
      ["/accounts", f.accounts[0].name],
    ];
    // controle positivo: com valores visíveis há dinheiro nas páginas com valores
    for (const path of ["/", "/transactions", "/pending", "/accounts"]) {
      await visit(router, path);
      expect(bodyText(), `${path} visível`).toMatch(MONEY_LIKE);
    }
    await click(q("[data-testid=toggle-values]"));
    expect(localStorage.getItem("fd.hideValues")).toBe("1");
    for (const [path, anchor] of anchors) {
      await visit(router, path);
      expect(bodyText(), `${path} carregada`).toContain(anchor);
      expectNoMoney();
    }
    await visit(router, "/import");
    await uploadExample();
    expect(q("[role=tablist]")).not.toBeNull();
    for (const l of PREVIEW.lines) expect(bodyText()).toContain(l.description);
    expectNoMoney();
  });
});

describe("bootDemo", () => {
  it("shows_fallback_and_rethrows_when_boot_fails", async () => {
    const host = document.createElement("div");
    host.id = "app";
    document.body.appendChild(host);
    const err = vi.spyOn(console, "error").mockImplementation(() => {});
    try {
      // força falha no fim de createDemoApp
      vi.spyOn(document.documentElement, "setAttribute").mockImplementation(() => {
        throw new Error("falha de boot");
      });
      await expect(bootDemo()).rejects.toThrow("falha de boot");
      expect(host.textContent).toBe("Demo indisponível");
      expect(err).toHaveBeenCalled();
    } finally {
      host.remove();
    }
  });
});
