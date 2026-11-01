"""
Чинит сгенерированный TDLib C++/CX API (TdDotNetApi.h).

Баг генератора: если в классе есть свойство с именем, совпадающим с типом API
(например `Message`), то последующее `Array<Message^>^` внутри того же класса
уже указывает на свойство, а не на тип -> error C2327.
Генератор TDLib сам обходит это только для пары захардкоженных случаев
(WebPage.Stickers, ReplyToMessage), новый UpdateNewGuestQuery.ReferenceMessages забыли.

Делаем то же, что генератор: полностью квалифицируем тип и разворачиваем свойство
в явные get/set с приватным полем. Скрипт идемпотентный.
"""
import re
import sys

path = sys.argv[1]
src = open(path, encoding="utf-8").read()

class_re = re.compile(r"(public ref class (\w+) sealed[^{]*\{)(.*?)(\n\};)", re.S)
prop_re = re.compile(r"^  property ([\w:<>^]+) (\w+)( \{|;)", re.M)
array_prop_re = re.compile(r"^  property Array<(\w+)\^>\^ (\w+);\n", re.M)

fixed = []

def fix_class(m):
    head, cls, body, tail = m.group(1), m.group(2), m.group(3), m.group(4)
    names = set(p.group(2) for p in prop_re.finditer(body))

    def repl(pm):
        elem, field = pm.group(1), pm.group(2)
        if elem not in names:
            return pm.group(0)
        t = "Array<::Telegram::Td::Api::%s^>^" % elem
        fixed.append("%s.%s" % (cls, field))
        return (
            "private:\n"
            "  %s %sPrivateField;\n"
            "public:\n"
            "  property %s %s {\n"
            "    %s get() {\n"
            "      return %sPrivateField;\n"
            "    }\n"
            "    void set(%s newValue) {\n"
            "      %sPrivateField = newValue;\n"
            "    }\n"
            "  }\n"
        ) % (t, field, t, field, t, field, t, field)

    return head + array_prop_re.sub(repl, body) + tail

out = class_re.sub(fix_class, src)
if fixed:
    open(path, "w", encoding="utf-8", newline="").write(out)
    print("fixed:", ", ".join(fixed))
else:
    print("nothing to fix (already patched or not needed)")
