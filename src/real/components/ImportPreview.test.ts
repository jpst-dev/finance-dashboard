import { beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import type { ImportPreview as Preview } from "../api/types";
import { useValuesVisibility } from "../money/visibility";
import { useAccountsStore } from "../stores/accounts";
import { useCategoriesStore } from "../stores/categories";
import { useImportsStore } from "../stores/imports";
import { fakeImportApi, line, ofx, preview } from "../testingImport";
import ImportPreview from "./ImportPreview.vue";

async function setup(p?: Preview) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const fake = fakeImportApi({ preview: p });
  await useAccountsStore().load();
  await useCategoriesStore().load();
  const store = useImportsStore();
  await store.upload("a1", ofx());
  const w = mount(ImportPreview, { global: { plugins: [pinia, i18n] }, attachTo: document.body });
  await flushPromises();
  return { w, store, ...fake };
}

const tabs = (w: Awaited<ReturnType<typeof setup>>["w"]) => w.findAll("[role=tab]");

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
  document.body.innerHTML = "";
});

describe("ImportPreview", () => {
  it("Tabs_keyboard_navigation", async () => {
    const { w } = await setup();
    expect(w.find("[role=tablist]").exists()).toBe(true);
    const t = tabs(w);
    expect(t).toHaveLength(4);
    expect(t[0].attributes("aria-selected")).toBe("true");
    expect(t[0].attributes("tabindex")).toBe("0");
    expect(t[1].attributes("tabindex")).toBe("-1");
    const panelId = t[0].attributes("aria-controls")!;
    expect(w.find(`#${panelId}`).attributes("role")).toBe("tabpanel");

    await t[0].trigger("keydown", { key: "ArrowRight" });
    expect(tabs(w)[1].attributes("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(tabs(w)[1].element);
    await tabs(w)[1].trigger("keydown", { key: "End" });
    expect(tabs(w)[3].attributes("aria-selected")).toBe("true");
    await tabs(w)[3].trigger("keydown", { key: "ArrowRight" });
    expect(tabs(w)[0].attributes("aria-selected")).toBe("true");
    await tabs(w)[0].trigger("keydown", { key: "ArrowLeft" });
    expect(tabs(w)[3].attributes("aria-selected")).toBe("true");
    await tabs(w)[3].trigger("keydown", { key: "Home" });
    expect(tabs(w)[0].attributes("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(tabs(w)[0].element);
  });

  it("tabs_show_counts_and_confirm_uses_server_new_count", async () => {
    const { w } = await setup(preview({ newCount: 9 }));
    const text = tabs(w).map((t) => t.text());
    expect(text[0]).toContain("Novos");
    expect(text[0]).toContain("2");
    expect(text[1]).toContain("1");
    expect(text[2]).toContain("1");
    expect(text[3]).toContain("0");
    expect(w.find("[data-test=confirm]").text()).toBe("Confirmar 9 novos");
  });

  it("pending_tab_rows_have_state_and_category_selects_bound_to_store", async () => {
    const { w, store } = await setup();
    await tabs(w)[1].trigger("click");
    const rows = w.findAll("[data-test=pending-line]");
    expect(rows).toHaveLength(1);
    expect(w.text()).toContain("fora do resumo mensal");
    await rows[0].find("[data-test=line-state]").setValue("Normal");
    expect(store.overrides.get("p1#0")).toEqual({ state: "Normal" });
    const cat = rows[0].find("select[data-test=line-category]");
    const opts = cat.findAll("option").map((o) => o.attributes("value"));
    expect(opts).toContain("c-exp");
    expect(opts).not.toContain("c-inc"); // valor negativo: só despesa
    await cat.setValue("c-exp");
    expect(store.overrides.get("p1#0")).toEqual({ state: "Normal", categoryId: "c-exp" });
    // Novos: somente leitura.
    await tabs(w)[0].trigger("click");
    expect(w.find("[data-test=new-lines] select").exists()).toBe(false);
  });

  it("Blocking_warning_requires_acknowledgement", async () => {
    const { w, imports } = await setup(
      preview({
        warnings: [
          { code: "FitidInOtherAccount", severity: "blocking", message: "2 line(s) of this file already exist in another account.", overridable: true },
          { code: "PeriodGap", severity: "warning", message: "gap", overridable: true },
        ],
      }),
    );
    expect(tabs(w)[3].text()).toContain("2");
    const btn = w.find("[data-test=confirm]");
    expect(btn.attributes("disabled")).toBeDefined();
    await tabs(w)[3].trigger("click");
    expect(w.findAll("[data-test=warning]")).toHaveLength(2);
    expect(w.text()).not.toContain("line(s)"); // texto do servidor não é exibido; traduzimos pelo código
    await btn.trigger("click");
    expect(imports.confirm).not.toHaveBeenCalled();
    await w.find("[data-test=ack]").setValue(true);
    expect(w.find("[data-test=confirm]").attributes("disabled")).toBeUndefined();
    await w.find("[data-test=confirm]").trigger("click");
    await flushPromises();
    expect(imports.confirm).toHaveBeenCalledWith("b1", { acknowledgeBlockingWarnings: true });
  });

  it("Descriptions_are_rendered_as_text_never_html", async () => {
    const evil = '<img src=x onerror="alert(1)">';
    const { w } = await setup(
      preview({
        lines: [
          line({ sourceKey: "n1", description: evil }),
          line({ sourceKey: "p1", description: evil, state: "PendingClassification" }),
          line({ sourceKey: "e1", description: evil, existing: true }),
        ],
      }),
    );
    for (let i = 0; i < 3; i++) {
      await tabs(w)[i].trigger("click");
      expect(w.find("img").exists()).toBe(false);
      expect(w.text()).toContain(evil);
    }
  });

  it("Amounts_masked_when_hidden", async () => {
    const { w } = await setup(preview({ checkpoint: { date: "2026-03-31", amount: "4321.09" } }));
    expect(w.text()).toMatch(/25,50/);
    expect(w.text()).toMatch(/4\.321,09/);
    useValuesVisibility().toggle();
    await flushPromises();
    for (let i = 0; i < 3; i++) {
      await tabs(w)[i].trigger("click");
      expect(w.text()).not.toMatch(/25,50|1\.000|40,00|5,00|4\.321/);
      expect(w.html()).not.toMatch(/25\.50|1000\.00|40\.00|4321\.09/);
    }
    useValuesVisibility().toggle();
  });

  it("Already_imported_preview_disables_confirm_and_uses_server_counts", async () => {
    const { w, imports } = await setup(
      preview({
        newCount: 0,
        existingCount: 0,
        warnings: [
          { code: "AlreadyImported", severity: "warning", message: "x", overridable: true },
          { code: "FitidInOtherAccount", severity: "blocking", message: "y", overridable: true },
        ],
      }),
    );
    const t = tabs(w).map((x) => x.text());
    expect(t[0]).toMatch(/Novos\s*0/);
    expect(t[1]).toMatch(/Pendentes\s*0/);
    expect(t[2]).toMatch(/existentes\s*0/);
    expect(w.find("[data-test=already-imported]").text()).toContain("nada será criado");
    expect(w.text()).not.toContain("Lançamentos que serão criados");
    expect(w.find("[data-test=ack]").exists()).toBe(false);
    expect(w.find("[data-test=confirm]").attributes("disabled")).toBeDefined();
    expect(w.find("[data-test=cancel]").attributes("disabled")).toBeUndefined();
    await tabs(w)[1].trigger("click");
    expect(w.find("[data-test=line-state]").exists()).toBe(false);
    await w.find("[data-test=confirm]").trigger("click");
    expect(imports.confirm).not.toHaveBeenCalled();
  });
});
