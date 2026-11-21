const copyStatus = document.querySelector("#copy-status");

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
