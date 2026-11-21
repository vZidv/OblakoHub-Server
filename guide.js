const copyStatus = document.querySelector("#copy-status");
const themeSelect = document.querySelector("#theme");
const themeKey = "oblakohub-guide-theme";

function applyTheme(theme) {
  const explicit = theme === "light" || theme === "dark";
  themeSelect.value = explicit ? theme : "system";
  if (explicit) document.documentElement.dataset.theme = theme;
  else delete document.documentElement.dataset.theme;
}

let savedTheme;
try { savedTheme = localStorage.getItem(themeKey); } catch { /* Storage may be disabled. */ }
applyTheme(savedTheme);
themeSelect.parentElement.hidden = false;
themeSelect.addEventListener("change", () => {
  applyTheme(themeSelect.value);
  try { localStorage.setItem(themeKey, themeSelect.value); } catch { /* Apply for this visit. */ }
});

if (navigator.clipboard) {
  document.querySelectorAll(".command button").forEach((button) => {
    const code = button.closest(".command").querySelector("code");
    let resetTimer;
    button.hidden = false;
    button.addEventListener("click", async () => {
      clearTimeout(resetTimer);
      try {
        await navigator.clipboard.writeText(code.textContent);
        button.textContent = "Скопировано";
        copyStatus.textContent = "Команды скопированы.";
      } catch {
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents(code);
        selection.removeAllRanges();
        selection.addRange(range);
        button.textContent = "Текст выделен";
        copyStatus.textContent = "Скопируй выделенные команды вручную.";
      }
      resetTimer = setTimeout(() => {
        button.textContent = "Скопировать";
        copyStatus.textContent = "";
      }, 2500);
    });
  });
}
