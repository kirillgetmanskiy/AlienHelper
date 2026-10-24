# Microsoft Store: restricted capabilities

Partner Center shows:

> Package acceptance validation warning: The following restricted capabilities require approval before you can
> use them in your app: runFullTrust, allowElevation.

This is not an error: both capabilities are reviewed by hand. Enter the reasons in the submission under
**Submission options → Restricted capabilities** ("Why do you need the … capability(ies)?"), then submit for
certification. The texts below can be pasted as they are.

Package: `installer\Output\AlienHelper-0.1.2.msix` (built by `installer\build-msix.ps1`).

## Text for Partner Center (English)

**runFullTrust**

> Alien Helper is a classic Win32 desktop application (.NET 8, Windows Forms) packaged with the Desktop Bridge.
> runFullTrust is required to run it as a full-trust desktop process: it reads hardware sensors through WMI,
> Windows performance counters, the NVIDIA driver (NVML/NVAPI) and USB HID, none of which is available to an
> AppContainer app.

**allowElevation**

> Alien Helper controls the hardware of Alienware laptops: power modes, fan speed and fan curves, keyboard and
> chassis lighting. The laptop firmware exposes these controls only through the ACPI WMI class
> root\WMI\AWCCWmiMethodFunction, whose methods can be called by administrators only; game frame-rate measurement
> uses an ETW kernel session, which also requires administrator rights. The app starts without elevation and asks
> for it through the standard UAC prompt every time; it never bypasses UAC.
> It installs no services or drivers and only calls the same firmware methods Alienware Command Center uses.
> Without elevation the app cannot read the fans or change power modes and fan speed, which is its core function,
> so allowElevation is required.

## If allowElevation is refused

The Store also accepts classic installers (EXE/MSI) instead of MSIX: `installer\Output\AlienHelper-Setup-<version>.exe`
(Inno Setup, silent switch `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`). That route has no restricted capabilities,
but the installer must be hosted at a fixed HTTPS URL and signed with a code-signing certificate from a trusted CA
(for example Azure Trusted Signing); see `installer\build-installer.ps1 -SignCmd`.
