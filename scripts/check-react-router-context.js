const path = require('path');

function resolveRouterFrom(packageName) {
  const entryPoint = require.resolve(packageName);
  return require.resolve('react-router', {
    paths: [path.dirname(entryPoint)],
  });
}

const connectedRouter = resolveRouterFrom('connected-react-router');
const routeComponents = resolveRouterFrom('react-router-dom');

if (connectedRouter !== routeComponents) {
  console.error(
    'The frontend resolved ConnectedRouter and react-router-dom to separate React Router instances.'
  );
  console.error(`ConnectedRouter: ${connectedRouter}`);
  console.error(`react-router-dom: ${routeComponents}`);
  process.exit(1);
}
