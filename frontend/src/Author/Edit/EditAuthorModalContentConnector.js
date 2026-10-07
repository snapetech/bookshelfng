import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { saveAuthor, setAuthorValue } from 'Store/Actions/authorActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import EditAuthorModalContent from './EditAuthorModalContent';

function createIsPathChangingSelector() {
  return createSelector(
    (state) => state.authors.pendingChanges,
    createAuthorSelector(),
    (pendingChanges, author) => {
      const path = pendingChanges.path;

      if (path == null) {
        return false;
      }

      return author.path !== path;
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    (state) => state.authors,
    (state) => state.settings.metadataProfiles,
    (state) => state.settings.qualityProfiles,
    createAuthorSelector(),
    createIsPathChangingSelector(),
    (authorsState, metadataProfiles, qualityProfiles, author, isPathChanging) => {
      const {
        isSaving,
        saveError,
        pendingChanges
      } = authorsState;

      const authorSettings = {
        ..._.pick(author, [
          'monitored',
          'monitorNewItems',
          'qualityProfileId',
          'metadataProfileId',
          'ebookQualityProfileId',
          'audiobookQualityProfileId',
          'ebookMetadataProfileId',
          'audiobookMetadataProfileId',
          'path',
          'ebookPath',
          'audiobookPath',
          'tags'
        ]),
        ebookQualityProfileId: author.ebookQualityProfileId ?? null,
        audiobookQualityProfileId: author.audiobookQualityProfileId ?? null,
        ebookMetadataProfileId: author.ebookMetadataProfileId ?? null,
        audiobookMetadataProfileId: author.audiobookMetadataProfileId ?? null,
        ebookPath: author.ebookPath ?? '',
        audiobookPath: author.audiobookPath ?? ''
      };

      const settings = selectSettings(authorSettings, pendingChanges, saveError);

      return {
        authorName: author.authorName,
        isSaving,
        saveError,
        isPathChanging,
        originalPath: author.path,
        item: settings.settings,
        showMetadataProfile: metadataProfiles.items.length > 1,
        showFormatMetadataProfiles: metadataProfiles.items.length > 1,
        showFormatQualityProfiles: qualityProfiles.items.length > 1,
        ...settings
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchSetAuthorValue: setAuthorValue,
  dispatchSaveAuthor: saveAuthor
};

class EditAuthorModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidUpdate(prevProps, prevState) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalClose();
    }
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.dispatchSetAuthorValue({ name, value });
  };

  onSavePress = (moveFiles) => {
    this.props.dispatchSaveAuthor({
      id: this.props.authorId,
      moveFiles
    });
  };

  //
  // Render

  render() {
    return (
      <EditAuthorModalContent
        {...this.props}
        onInputChange={this.onInputChange}
        onSavePress={this.onSavePress}
        onMoveAuthorPress={this.onMoveAuthorPress}
      />
    );
  }
}

EditAuthorModalContentConnector.propTypes = {
  authorId: PropTypes.number,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  dispatchSetAuthorValue: PropTypes.func.isRequired,
  dispatchSaveAuthor: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(EditAuthorModalContentConnector);
