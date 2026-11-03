import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { ApiError } from "../api/errors";
import type { Category } from "../api/types";
import { acc, fakeAccountsApi } from "../testing";
import { useAccountsStore } from "./accounts";
import { useCategoriesStore } from "./categories";
import { todayISO, todayLocalISO } from "../money/today";
import { setAppConfig } from "../appConfig";

const cat = (id: string, kind: "Income" | "Expense"): Category => ({ id, name: id, kind, isDefault: false });

beforeEach(() => setActivePinia(createPinia()));

describe("categories.forAmount", () => {
  async function store() {
    fakeAccountsApi([], [cat("sal", "Income"), cat("mer", "Expense")]);
    const s = useCategoriesStore();
    await s.load();
    return s;
  }
  it("forAmount_uses_sign_of_the_string", async () => {
    const s = await store();
    expect(s.forAmount("-0.01").map((c) => c.id)).toEqual(["mer"]);
    expect(s.forAmount("-1234.56").map((c) => c.id)).toEqual(["mer"]);
    expect(s.forAmount("5").map((c) => c.id)).toEqual(["sal"]);
    expect(s.forAmount("0.01").map((c) => c.id)).toEqual(["sal"]);
  });
  it("forAmount_zero_or_invalid_returns_all", async () => {
    const s = await store();
    for (const v of ["0.00", "-0.00", "0", "", "abc", "+5", "1,5", "1.234"]) expect(s.forAmount(v)).toHaveLength(2);
  });
});

describe("accounts store", () => {
  it("archived_accounts_move_to_archived_section", async () => {
    const { accounts } = fakeAccountsApi([acc(), acc({ id: "a2", name: "Outra", isArchived: true })]);
    const s = useAccountsStore();
    await s.load();
    expect(s.active.map((a) => a.id)).toEqual(["a1"]);
    expect(s.archived.map((a) => a.id)).toEqual(["a2"]);
    await s.setArchived("a1", true);
    expect(accounts.update).toHaveBeenCalledWith("a1", expect.objectContaining({ name: "Conta Exemplo", type: "Checking", isArchived: true }));
    expect(s.active).toHaveLength(0);
    expect(s.archived.map((a) => a.id).sort()).toEqual(["a1", "a2"]);
  });

  it("create_account_trims_and_validates_name_1_to_80", async () => {
    const { accounts } = fakeAccountsApi([]);
    const s = useAccountsStore();
    expect(await s.create({ name: "   ", type: "Wallet" })).toBeNull();
    expect(s.error).toBe("invalid_name");
    expect(await s.create({ name: "x".repeat(81), type: "Wallet" })).toBeNull();
    expect(accounts.create).not.toHaveBeenCalled();
    expect(await s.create({ name: "  Conta Exemplo  ", type: "Wallet", institution: "  ", externalIdentifier: " 123 " })).not.toBeNull();
    expect(accounts.create).toHaveBeenCalledWith({ name: "Conta Exemplo", type: "Wallet", institution: null, externalIdentifier: "123" });
    expect(s.error).toBeNull();
    expect(await s.create({ name: "y".repeat(80), type: "Wallet" })).not.toBeNull();
  });

  it("keeps_error_as_code_and_busy_resets", async () => {
    const { accounts } = fakeAccountsApi([]);
    accounts.list.mockRejectedValueOnce(new ApiError(0));
    const s = useAccountsStore();
    expect(await s.load()).toBe(false);
    expect(s.error).toBe("network");
    expect(s.busy).toBe(false);
    accounts.create.mockRejectedValueOnce(new ApiError(400, "secret-1234.56"));
    await s.create({ name: "A", type: "Wallet" });
    expect(s.error).toBe("invalid");
  });

  it("rename_and_archive_preserve_institution_and_external_identifier", async () => {
    const { accounts } = fakeAccountsApi([acc({ institution: "Banco Exemplo", externalIdentifier: "ext-1" })]);
    const s = useAccountsStore();
    await s.load();
    await s.rename("a1", "Novo Nome");
    expect(accounts.update).toHaveBeenLastCalledWith("a1", expect.objectContaining({ name: "Novo Nome", institution: "Banco Exemplo", externalIdentifier: "ext-1" }));
    await s.setArchived("a1", true);
    expect(accounts.update).toHaveBeenLastCalledWith("a1", expect.objectContaining({ institution: "Banco Exemplo", externalIdentifier: "ext-1", isArchived: true }));
  });

  it("run_ignores_reentrant_calls_while_busy", async () => {
    const { accounts } = fakeAccountsApi([acc()]);
    const s = useAccountsStore();
    await s.load();
    let release!: () => void;
    accounts.update.mockImplementationOnce(() => new Promise((r) => (release = () => r(acc({ name: "B" })))));
    const first = s.rename("a1", "B");
    expect(await s.rename("a1", "C")).toBeNull();
    release();
    await first;
    expect(accounts.update).toHaveBeenCalledTimes(1);
  });

  it("add_checkpoint_sends_strings_and_reloads_balances", async () => {
    const { accounts } = fakeAccountsApi([acc()]);
    const s = useAccountsStore();
    await s.load();
    accounts.list.mockResolvedValueOnce([acc({ balance: "250.00" })]);
    await s.addCheckpoint("a1", "2026-11-03", "250.00");
    expect(accounts.addCheckpoint).toHaveBeenCalledWith("a1", { date: "2026-11-03", amount: "250.00" });
    expect(s.items[0].balance).toBe("250.00");
  });
});

describe("todayLocalISO", () => {
  it("uses_local_calendar_date", () => {
    expect(todayLocalISO(new Date(2026, 0, 5, 23, 59))).toBe("2026-01-05");
  });

  describe("todayISO", () => {
    afterEach(() => {
      setAppConfig(null);
      vi.useRealTimers();
    });
    it("todayISO_uses_fixed_date_when_configured", () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2027, 2, 1, 10));
      setAppConfig({ fixedToday: "2026-10-15" });
      expect(todayISO()).toBe("2026-10-15");
    });
    it("todayISO_without_config_is_local_date", () => {
      expect(todayISO()).toBe(todayLocalISO());
    });
  });
});
