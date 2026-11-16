"""Dump type/field/method names from an Il2CppInterop assembly.

Usage: python dump_types.py <assembly.dll> [regex-filter-on-type-name]
"""

import re
import sys

import dnfile


def main() -> None:
    path = sys.argv[1]
    pat = re.compile(sys.argv[2], re.I) if len(sys.argv) > 2 else None
    pe = dnfile.dnPE(path)
    md = pe.net.mdtables
    for td in md.TypeDef:
        name = f"{td.TypeNamespace}.{td.TypeName}".lstrip(".")
        if pat and not pat.search(name):
            continue
        base = ""
        if td.Extends and td.Extends.row is not None:
            r = td.Extends.row
            base = getattr(r, "TypeName", "")
        print(f"\n== {name} : {base}")
        for f in td.FieldList:
            fn = str(f.row.Name)
            if fn.startswith("NativeFieldInfoPtr") or fn.startswith("NativeMethodInfoPtr"):
                continue
            print(f"   F {fn}")
        for m in td.MethodList:
            mn = str(m.row.Name)
            if mn.startswith(("get_", "set_", ".c", "add_", "remove_")):
                continue
            print(f"   M {mn}")


if __name__ == "__main__":
    main()
