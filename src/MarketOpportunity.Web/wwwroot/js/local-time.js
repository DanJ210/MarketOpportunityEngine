const localTimeFormatter = new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    timeZoneName: "short"
});

export function formatLocalTimes() {
    for (const element of document.querySelectorAll("time[data-local-time]")) {
        if (!element.dateTime) {
            continue;
        }

        const value = new Date(element.dateTime);
        if (Number.isNaN(value.getTime())) {
            continue;
        }

        element.textContent = localTimeFormatter.format(value);
        element.title = Intl.DateTimeFormat().resolvedOptions().timeZone;
    }
}
