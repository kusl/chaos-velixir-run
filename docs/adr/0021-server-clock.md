# 0021 Server clock in the footer

Status: accepted

- `MainLayout` renders the server's local time, Unix ms and UTC offset into `<time id="server-clock" data-permanent>`. No circuit is needed.
- `App.razor.js` turns that into an offset from `performance.now()`, using the navigation timing window (`requestStart`–`responseStart`) as the error bound, and ticks once per second. The device's wall clock is never shown.
- It resyncs from `GET /time` (`{ unixMs, offsetMinutes }`, `no-store`) on load, every 10 minutes, when the tab becomes visible and when wall time and monotonic time drift apart by over 2 s (sleep). A sample replaces the current one if its error is smaller than the current error plus 100 ppm of its age; forced resyncs always replace it.
