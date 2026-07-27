namespace RegistroPontosSSG.Core.Automation;

/// <summary>
/// Seletores da interface do SSG (SPA AngularJS em https://ssg.sysmap.com.br/index.html).
///
/// A tela antiga (<c>/new/timesheet/timesheetrecording.asp</c>, tabela <c>#TableTimesheet</c>)
/// foi substituída pela rota <c>#/access-entry/get-list</c>, onde cada dia do período é
/// renderizado como um card <c>.access-entry-day[data-date="DD/MM/YYYY"]</c> contendo dois
/// painéis: "Registro de E-S" e "Apontamento".
///
/// Todos os seletores abaixo são baseados em classes próprias da aplicação
/// (<c>button-*</c>, <c>input-*</c>, <c>table-*</c>), evitando XPath posicional.
/// </summary>
internal static class SsgSelectors
{
    // ---- Login (portal WordPress, inalterado) ----
    public const string LoginUser = "#user_login";
    public const string LoginPassword = "#user_pass";
    public const string LoginTotp = "#googleotp";
    public const string LoginSubmit = "#wp-submit, input[name=\"wp-submit\"], input[type=\"submit\"], button[type=\"submit\"]";

    // ---- Filtro de período ----
    // O filtro é renderizado duas vezes (variante desktop .hidden-xs e variante mobile),
    // por isso todos os seletores usam :visible — sem isso o .First cai no elemento
    // invisível e o clique estoura timeout.
    public const string DateRangeComponent = ".date-range-component";
    public const string DateRangeToggle = ".date-range-component a.dropdown-toggle:visible";
    public const string DateRangeCurrentMonth = ".date-range-component a.button-current-month:visible";
    public const string DateRangePreviousMonth = ".date-range-component a.button-previous-month:visible";
    public const string FilterButton = "button.button-filter:visible";
    public const string StartDate = "input.start-date:visible";
    public const string EndDate = "input.end-date:visible";

    // ---- Cards de dia ----
    public const string DayCard = ".access-entry-day";
    public const string DayBody = ".day-body";
    public const string DayToggle = "button.button-toggle-day";
    public const string DayStatus = ".day-status";
    public const string AttrDate = "data-date";
    public const string AttrAccessAllowed = "data-access-entry-allowed";
    public const string AttrValidDate = "data-is-valid-date";

    // ---- Painel "Registro de E-S" ----
    public const string AccessTable = ".table-access-records";
    /// <summary>Linhas reais de E/S (exclui <c>.access-record-row-template.hide</c>).</summary>
    public const string AccessRows = ".table-access-records tbody tr:not(.hide)";
    public const string AddAccessRow = "button.button-add-access-row:visible";
    public const string RemoveAccessRow = "a.button-remove-access-row, button.button-remove-access-row";
    public const string ClockIn = "input.input-clock-in";
    public const string ClockOut = "input.input-clock-out";
    public const string AccessNote = "input.input-access-note";
    public const string AccessTotalHours = ".access-total-hours";

    // ---- Painel "Apontamento" ----
    public const string AppointmentTable = ".table-appointments";
    /// <summary>Linhas reais de apontamento (exclui <c>.appointment-row-template.hide</c>).</summary>
    public const string AppointmentRows = ".table-appointments tbody tr:not(.hide)";
    public const string AddAppointmentRow = "button.button-add-appointment-row:visible";
    public const string AppointedHours = "input.input-appointed-hours";
    public const string ProjectActivity = "input.input-project-activity";
    public const string ShowItemsButton = "button.button-show-items";
    public const string AppointmentTotalHours = ".appointment-total-hours";

    // ---- Autocomplete / modal de itens ----
    public const string TypeaheadItems = "ul.typeahead.dropdown-menu li, ul.typeahead li";
    public const string ItemsModal = ".modal-list-items";
    public const string ItemsModalRows = ".modal-list-items table.table-items-autocomplete tbody tr";
    public const string ItemsModalSelect = "button.button-select, a.button-select";
    public const string ItemsModalClose = ".modal-list-items button.button-close";

    // ---- Salvar / modais ----
    public const string SaveButton = "button.button-save-access-entry:visible";
    public const string BootboxPrimary = ".bootbox button.btn-primary, div.modal.in .modal-footer button.btn-primary";
    /// <summary>
    /// Qualquer modal visível. Um <c>.modal-list-items</c> deixado aberto mantém o
    /// <c>.modal-backdrop</c> na tela e bloqueia todos os cliques seguintes.
    /// </summary>
    public const string AnyVisibleModal = "div.modal.in, .bootbox.modal.in";
    public const string ModalCloseButtons = "button.button-close, button.close, .modal-footer button.btn-primary";
}
