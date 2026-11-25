#!/usr/bin/env python3
"""Fails unless every SQL Server integration test in the TRX files actually ran and passed.

A skipped (NotExecuted) SQL test is a failure: a green run with skipped SQL tests is not SQL verification.
Prints a summary (markdown) to stdout and writes it to the path in --summary. Never prints connection strings.
"""
import argparse, glob, re, sys, xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SQL_CLASS = re.compile(r"\.(ScratchSuiteTests|\w+SqlServerTests|\w+SqlServerCi\w*)$")


def load(paths):
    rows = []
    for p in paths:
        root = ET.parse(p).getroot()
        defs = {}
        for ut in root.iterfind(".//t:UnitTest", NS):
            tm = ut.find("t:TestMethod", NS)
            if tm is not None:
                defs[ut.get("id")] = tm.get("className", "")
        for r in root.iterfind(".//t:UnitTestResult", NS):
            msg = r.find(".//t:ErrorInfo/t:Message", NS)
            rows.append({
                "name": r.get("testName", ""),
                "outcome": r.get("outcome", ""),
                "cls": defs.get(r.get("testId"), ""),
                "msg": (msg.text or "").strip()[:400] if msg is not None else "",
            })
    return rows


def evaluate(rows, min_sql):
    sql = [r for r in rows if SQL_CLASS.search(r["cls"])]
    count = lambda rs, o: sum(1 for r in rs if r["outcome"] == o)
    problems = []
    if len(sql) < min_sql:
        problems.append(f"only {len(sql)} SQL Server tests found, expected at least {min_sql}")
    for r in sql:
        if r["outcome"] != "Passed":
            problems.append(f"{r['outcome']}: {r['cls'].rsplit('.', 1)[-1]}.{r['name']} {r['msg']}".strip())
    if count(rows, "Failed"):
        problems += [f"Failed (any project test): {r['name']} {r['msg']}".strip() for r in rows if r["outcome"] == "Failed" and r not in sql]
    lines = [
        "## SQL Server integration test summary",
        "",
        "| | Total | Passed | Failed | Skipped |",
        "|---|---|---|---|---|",
        f"| All tests in the SQL project | {len(rows)} | {count(rows,'Passed')} | {count(rows,'Failed')} | {count(rows,'NotExecuted')} |",
        f"| SQL Server integration tests | {len(sql)} | {count(sql,'Passed')} | {count(sql,'Failed')} | {count(sql,'NotExecuted')} |",
        "",
        "SQL Server integration tests executed: %d. Skipped: %d." % (count(sql, "Passed") + count(sql, "Failed"), count(sql, "NotExecuted")),
        "",
    ]
    if sql:
        lines += ["| Test | Outcome |", "|---|---|"] + [f"| {r['cls'].rsplit('.',1)[-1]}.{r['name']} | {r['outcome']} |" for r in sql] + [""]
    if problems:
        lines += ["### FAILED CHECKS", ""] + [f"- {p}" for p in problems]
    else:
        lines += ["All SQL Server integration tests executed and passed."]
    return problems, "\n".join(lines)


def self_test():
    ok = [{"name": "a", "outcome": "Passed", "cls": "X.ScratchSuiteTests", "msg": ""}]
    skipped = [{"name": "a", "outcome": "NotExecuted", "cls": "X.ScratchSuiteTests", "msg": ""}]
    failed = [{"name": "a", "outcome": "Failed", "cls": "X.ScratchSuiteTests", "msg": "boom"}]
    assert not evaluate(ok, 1)[0]
    assert evaluate(skipped, 1)[0], "skipped SQL test must fail the check"
    assert evaluate(failed, 1)[0], "failed SQL test must fail the check"
    assert evaluate(ok, 2)[0], "too few SQL tests must fail the check"
    print("check_sql_results self-test passed: skipped, failed and missing SQL tests are all rejected")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--trx-glob")
    ap.add_argument("--min-sql", type=int, default=14)
    ap.add_argument("--summary")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        self_test(); sys.exit(0)
    files = glob.glob(a.trx_glob, recursive=True)
    if not files:
        print("No TRX files found: the SQL tests did not run."); sys.exit(1)
    problems, text = evaluate(load(files), a.min_sql)
    print(text)
    if a.summary:
        open(a.summary, "w", encoding="utf8").write(text)
    sys.exit(1 if problems else 0)
