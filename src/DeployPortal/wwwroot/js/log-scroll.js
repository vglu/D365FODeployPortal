// W2.1 (EPIC-UI-OVERLAP-FIX): tiny auto-scroll helper for the
// virtualized deployment-log pane. The pane is an ElementReference
// passed from Blazor (DeploymentDetail.razor); we just nudge its
// scrollTop to scrollHeight whenever a new log row arrives AND the
// user has the auto-scroll toggle on. Works with <Virtualize> because
// the inner content extends beyond the viewport via spacers.
window.deployPortalLog = (function () {
  function scrollToBottom(el) {
    if (!el) return;
    // requestAnimationFrame: wait for Virtualize to lay out the new row
    // before measuring scrollHeight, otherwise we land one row short.
    requestAnimationFrame(function () {
      el.scrollTop = el.scrollHeight;
    });
  }

  // Returns true if the user has manually scrolled away from the
  // bottom. Caller uses this to decide whether to keep auto-scroll on
  // when the user grabs the scrollbar themselves.
  function isAtBottom(el, tolerancePx) {
    if (!el) return true;
    var t = typeof tolerancePx === 'number' ? tolerancePx : 16;
    return el.scrollHeight - el.scrollTop - el.clientHeight <= t;
  }

  return { scrollToBottom: scrollToBottom, isAtBottom: isAtBottom };
})();
