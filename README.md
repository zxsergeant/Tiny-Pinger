# Tiny Pinger

Небольшая утилита для мониторинга доступности сетевых узлов в Windows.

## Возможности

* мониторинг нескольких узлов;
* отображение состояния и времени ответа;
* журнал результатов проверки;
* компактное плавающее окно;
* режим «Поверх»;
* сохранение положения и размера окна.

## Компиляция

Для компиляции используется стандартный компилятор C# из .NET Framework 4:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:TinyPinger.exe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll TinyPinger.cs
```

## Лицензия

MIT License.
