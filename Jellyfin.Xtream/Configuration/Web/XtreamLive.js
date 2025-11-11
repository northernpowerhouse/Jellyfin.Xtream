export default function (view) {
  view.addEventListener("viewshow", () => import(
    ApiClient.getUrl("web/ConfigurationPage", {
      name: "Xtream.js",
    })
  ).then((Xtream) => Xtream.default
  ).then((Xtream) => {
    const pluginId = Xtream.pluginConfig.UniqueId;
    Xtream.setTabs(1);

    const getConfig = ApiClient.getPluginConfiguration(pluginId);
    const visible = view.querySelector("#Visible");
    const forceDirect = view.querySelector("#ForceDirectPlayCatchup");
    getConfig.then((config) => {
      visible.checked = config.IsCatchupVisible;
      if (forceDirect) {
        forceDirect.checked = !!config.ForceDirectPlayCatchup;
      }
    });
    const table = view.querySelector('#LiveContent');
    Xtream.populateCategoriesTable(
      table,
      () => getConfig.then((config) => config.LiveTv),
      () => Xtream.fetchJson('Xtream/LiveCategories'),
      (categoryId) => Xtream.fetchJson(`Xtream/LiveCategories/${categoryId}`),
    ).then((data) => {
      view.querySelector('#XtreamLiveForm').addEventListener('submit', (e) => {
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(pluginId).then((config) => {
          config.IsCatchupVisible = visible.checked;
          if (forceDirect) {
            config.ForceDirectPlayCatchup = forceDirect.checked;
          }
          config.LiveTv = data;
          ApiClient.updatePluginConfiguration(pluginId, config).then((result) => {
            Dashboard.processPluginConfigurationUpdateResult(result);
          });
        });

        e.preventDefault();
        return false;
      });
    });
  }));
}