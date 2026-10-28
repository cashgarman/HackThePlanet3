import sqlite3
import json

db = r"i:\IfYouCanJoinThemFuckThem\memory.db"
conn = sqlite3.connect(db)
conn.row_factory = sqlite3.Row
cur = conn.cursor()
cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
tables = [r[0] for r in cur.fetchall()]
print("TABLES:", tables)
for t in tables:
    cur.execute(f"PRAGMA table_info({t})")
    print(f"SCHEMA {t}:", [dict(r) for r in cur.fetchall()])

for t in tables:
    cur.execute(f"SELECT * FROM {t} WHERE id = 940 OR rowid = 940 LIMIT 5")
    rows = cur.fetchall()
    if rows:
        print(f"ROW in {t} id/rowid 940:")
        for r in rows:
            print(dict(r))
