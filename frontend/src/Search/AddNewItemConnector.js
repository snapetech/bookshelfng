import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { clearSearchResults, getSearchResults } from 'Store/Actions/searchActions';
import { fetchDevelopmentSettings, fetchRootFolders } from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import parseUrl from 'Utilities/String/parseUrl';
import AddNewItem from './AddNewItem';

function createMapStateToProps() {
  return createSelector(
    (state) => state.search,
    (state) => state.authors.items.length,
    (state) => state.router.location,
    createSettingsSectionSelector('development'),
    (search, existingAuthorsCount, location, developmentSettings) => {
      const { params } = parseUrl(location.search);

      return {
        ...search,
        term: params.term,
        hasExistingAuthors: existingAuthorsCount > 0,
        metadataSource: developmentSettings.metadataSource?.value
      };
    }
  );
}

const mapDispatchToProps = {
  getSearchResults,
  clearSearchResults,
  fetchRootFolders,
  fetchDevelopmentSettings
};

class AddNewItemConnector extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this._searchTimeout = null;
  }

  componentDidMount() {
    this.props.fetchRootFolders();
    this.props.fetchDevelopmentSettings();
  }

  componentWillUnmount() {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
    }

    this.props.clearSearchResults();
  }

  //
  // Listeners

  onSearchChange = (term) => {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
    }

    if (term.trim() === '') {
      this.props.clearSearchResults();
    } else {
      this._searchTimeout = setTimeout(() => {
        this.props.getSearchResults({ term });
      }, 300);
    }
  };

  onClearSearch = () => {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
      this._searchTimeout = null;
    }

    this.props.clearSearchResults();
  };

  //
  // Render

  render() {
    const {
      term,
      ...otherProps
    } = this.props;

    return (
      <AddNewItem
        term={term}
        {...otherProps}
        onSearchChange={this.onSearchChange}
        onClearSearch={this.onClearSearch}
      />
    );
  }
}

AddNewItemConnector.propTypes = {
  term: PropTypes.string,
  getSearchResults: PropTypes.func.isRequired,
  clearSearchResults: PropTypes.func.isRequired,
  fetchRootFolders: PropTypes.func.isRequired,
  fetchDevelopmentSettings: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AddNewItemConnector);
