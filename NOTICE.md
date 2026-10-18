# AlienHelper — notice / уведомление

Copyright (C) 2026 Kirill Getmanskiy

AlienHelper is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License version 3 or (at your option) any later version. See `LICENSE`.

### Additional terms (GPL section 7)

Under section 7(e) of the GPL, no rights are granted to use the name "AlienHelper" or its icon
(`src/AlienHelper/Assets/app.ico`) as a trade name, product name or store listing for modified versions.
A modified version that is distributed (including through the Microsoft Store or other app stores)
must use a different name and icon and must not suggest that it is the original or is endorsed
by the author. The source code itself, including the icon generator, stays under the GPL.

Согласно разделу 7(e) GPL, право использовать название «AlienHelper» и его иконку как название
продукта или карточки в магазине приложений для изменённых версий не предоставляется. Распространяемая
изменённая версия (в том числе в Microsoft Store) должна иметь другое название и иконку и не выдавать
себя за оригинал.

The author also distributes official builds through the Microsoft Store; the Store's own terms apply only
to that copy and do not restrict the rights this License gives you to the source code.

## No warranty, use at your own risk

AlienHelper talks to the laptop firmware (ACPI WMI) and to the lighting controllers (USB HID) directly.
It changes power modes, power limits (G-Mode), fan speeds and lighting. These interfaces are
undocumented and were reverse-engineered by third parties.

THE PROGRAM IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND (see sections 15–17 of the GPL).
The author is not liable for any damage to hardware, data loss, overheating, voided warranty
or any other consequence of using it. You use it entirely at your own risk.

Программа работает с прошивкой ноутбука (ACPI WMI) и контроллерами подсветки (USB HID) напрямую:
меняет режимы питания, лимиты мощности (G-Mode), обороты вентиляторов и подсветку. Эти интерфейсы
не документированы производителем.

ПРОГРАММА ПРЕДОСТАВЛЯЕТСЯ «КАК ЕСТЬ», БЕЗ КАКИХ-ЛИБО ГАРАНТИЙ (разделы 15–17 GPL). Автор не несёт
ответственности за повреждение оборудования, потерю данных, перегрев, потерю гарантии производителя
и любые другие последствия использования. Вы используете программу на свой страх и риск.

## Trademarks / товарные знаки

AlienHelper is an independent project. It is not affiliated with, endorsed or sponsored by Dell Inc.
Alienware, Alienware Command Center and AWCC are trademarks of Dell Inc. or its subsidiaries;
NVIDIA and Intel are trademarks of their owners. They are named only to describe compatibility.
The program does not use the Alienware logo.

AlienHelper — независимый проект, не связанный с Dell Inc. и не одобренный ею. Названия Alienware,
AWCC и другие принадлежат их владельцам и упоминаются только для описания совместимости.

## Third-party work

- Protocol knowledge: [T-Troll/alienfx-tools](https://github.com/T-Troll/alienfx-tools) and the Linux driver
  [`alienware-wmi-wmax.c`](https://github.com/torvalds/linux/blob/master/drivers/platform/x86/dell/alienware-wmi-wmax.c).
- NuGet packages (MIT): System.Management, System.Diagnostics.PerformanceCounter,
  Microsoft.Diagnostics.Tracing.TraceEvent. The released exe bundles the .NET runtime (MIT).
